﻿using DotNetty.Buffers;
using DotNetty.Codecs.Http;
using DotNetty.Codecs.Rtmp.AMF;
using DotNetty.Codecs.Rtmp.Messages;
using DotNetty.Common.Internal.Logging;
using DotNetty.Transport.Channels;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DotNetty.Codecs.Rtmp.Stream
{
	public class MediaStream
	{
		static readonly IInternalLogger logger = InternalLoggerFactory.GetInstance<MediaStream>();
		private readonly byte[] _flvHeader = new byte[] { 0x46, 0x4C, 0x56, 0x01, 0x05, 00, 00, 00, 0x09 };
		private VideoMessage _avcDecoderConfigurationRecord;
		private AudioMessage _aacAudioSpecificConfig;
		private StreamName _streamName;
		private FileStream _flvOutput;
		private List<AbstractRtmpMediaMessage> _content;
		private int? _videoTimestamp;
		private int? _audioTimestamp;
		private int? _obsTimeStamp;
		private bool _flvHeadAndMetadataWritten = false;
		protected readonly ConcurrentDictionary<string, IChannel> _subscribers;
		protected readonly ConcurrentDictionary<string, IChannel> _httpFLvSubscribers;

		public Dictionary<String, Object> Metadata { get; set; }

		public IChannel Publisher { get; set; }

		public MediaStream(StreamName streamName)
		{
			_subscribers = new ConcurrentDictionary<string, IChannel>();
			_httpFLvSubscribers = new ConcurrentDictionary<string, IChannel>();
			_content = new List<AbstractRtmpMediaMessage>();
			_streamName = streamName;
            if (RtmpConfig.Instance !=null &&  RtmpConfig.Instance.IsSaveFlvFile)
			CreateFileStream();
		}

		public void AddContent(AbstractRtmpMediaMessage msg,bool isClient=false)
		{
			if (!isClient)
			{
				if (_streamName.IsObsClient)
				{
					HandleObsStream(msg);
				}
				else
				{
					HandleNonObsStream(msg);
				}
			}
			if (msg is VideoMessage)
			{
				VideoMessage vm = (VideoMessage)msg;
				if (vm.IsAVCDecoderConfigurationRecord())
				{
					_avcDecoderConfigurationRecord = vm;
				}

				if (vm.IsH264KeyFrame())
				{
					_content.Clear();
                }
			}

			if (msg is AudioMessage)
			{
				AudioMessage am = (AudioMessage)msg;
				if (am.IsAACAudioSpecificConfig())
				{
					_aacAudioSpecificConfig = am;
				}
			}

			_content.Add(msg);
			if (RtmpConfig.Instance!=null && RtmpConfig.Instance.IsSaveFlvFile)
				WriteFlv(msg);
			BroadCastToSubscribers(msg);
		}

		private void HandleNonObsStream(AbstractRtmpMediaMessage msg)
		{
			if (msg is VideoMessage)
			{
				VideoMessage vm = (VideoMessage)msg;
				if (vm.Timestamp != null)
				{
					vm.TimestampDelta = (vm.Timestamp - _videoTimestamp);
					_videoTimestamp = vm.Timestamp.Value;
				}
				else if (vm.TimestampDelta != null)
				{
					_videoTimestamp += vm.TimestampDelta.Value;
					vm.Timestamp = _videoTimestamp;
				}
			}

			if (msg is AudioMessage)
			{

				AudioMessage am = (AudioMessage)msg;
				if (am.Timestamp != null)
				{
					am.TimestampDelta = (am.Timestamp - _audioTimestamp);
					_audioTimestamp = am.Timestamp.Value;
				}
				else if (am.TimestampDelta != null)
				{
					_audioTimestamp += am.TimestampDelta.Value;
					am.Timestamp = _audioTimestamp;
				}
			}
		}

		private void HandleObsStream(AbstractRtmpMediaMessage msg)
		{
			if (msg.Timestamp != null)
			{
				_obsTimeStamp = msg.Timestamp;
			}
			else if (msg.TimestampDelta != null)
			{
				_obsTimeStamp += msg.TimestampDelta;
			}
			msg.Timestamp = _obsTimeStamp;
			if (msg is VideoMessage)
			{
				msg.TimestampDelta = (_obsTimeStamp - _videoTimestamp);
				_videoTimestamp = _obsTimeStamp;
			}
			if (msg is AudioMessage)
			{
				msg.TimestampDelta = (_obsTimeStamp - _audioTimestamp);
				_audioTimestamp = _obsTimeStamp;
			}
		}

		private Memory<byte> EncodeMediaAsFlvTagAndPrevTagSize(AbstractRtmpMediaMessage msg)
		{
			int tagType = msg.GetMsgType();
			byte[] data = msg.Raw();
			int dataSize = data.Length;
			int timestamp = (msg.Timestamp ?? 0) & 0xffffff;
			int timestampExtended = (int)(((msg.Timestamp ?? 0) & 0xff000000) >> 24);

			var buffer = Unpooled.Buffer();

			buffer.WriteByte(tagType);
			buffer.WriteMedium(dataSize);
			buffer.WriteMedium(timestamp);
			buffer.WriteByte(timestampExtended);// timestampExtended
			buffer.WriteMedium(0);// streamid
			buffer.WriteBytes(data);
			buffer.WriteInt(data.Length + 11); // prevousTagSize

            Memory<byte> r = new Memory<byte>(new byte[buffer.ReadableBytes]);
			buffer.ReadBytes(r);
			return r;
		}

		private void WriteFlv(AbstractRtmpMediaMessage msg)
		{
			if (_flvOutput == null)
			{
				//logger.Error($"no flv file existed for stream : {_streamName}");
				return;
			}
			try
			{
				if (!_flvHeadAndMetadataWritten)
				{
					WriteFlvHeaderAndMetadata();
					_flvHeadAndMetadataWritten = true;
				}
                Memory<byte> encodeMediaAsFlv = EncodeMediaAsFlvTagAndPrevTagSize(msg);
				_flvOutput.Write(encodeMediaAsFlv.Span);
				_flvOutput.Flush();

			}
			catch (IOException e)
			{
				logger.Error($"stream:{_streamName} writting flv file failed",  e);
			}
		}

		private Memory<byte> EncodeFlvHeaderAndMetadata()
		{
			var encodeMetaData = EncodeMetaData();
			var buf = Unpooled.Buffer();

			var msg = _content[0];
			int timestamp = (msg.Timestamp ?? 0) & 0xffffff;
			int timestampExtended = (int)((msg.Timestamp & 0xff000000) >> 24);

			buf.WriteBytes(_flvHeader);
			buf.WriteInt(0); // previousTagSize0

			int readableBytes = encodeMetaData.ReadableBytes;
			buf.WriteByte(0x12); // script
			buf.WriteMedium(readableBytes);
			// make the first script tag timestamp same as the keyframe
			buf.WriteMedium(timestamp);
			buf.WriteByte(timestampExtended);
			//		buf.writeInt(0); // timestamp + timestampExtended
			buf.WriteMedium(0);// streamid
			buf.WriteBytes(encodeMetaData);
			buf.WriteInt(readableBytes + 11);

            Memory<byte> result = new Memory<byte>(new byte[buf.ReadableBytes]);
			buf.ReadBytes(result);

			return result;

		}

		private void WriteFlvHeaderAndMetadata()
		{
            Memory<byte> encodeFlvHeaderAndMetadata = EncodeFlvHeaderAndMetadata();
			_flvOutput.Write(encodeFlvHeaderAndMetadata.Span);
			_flvOutput.Flush();

		}

		private IByteBuffer EncodeMetaData()
		{
			logger.Info($"Metadata:{Metadata}");
			var buffer = Unpooled.Buffer();
			List<Object> meta = new List<object>(2);
			meta.Add("onMetaData");
			meta.Add(Metadata);
			AMF0.Encode(buffer, meta);
			return buffer;
		}

		private void CreateFileStream()
		{
			var path =Path.Combine( RtmpConfig.Instance.SaveFlvFilePath, _streamName.App + "_" + _streamName.Name);
			try
			{
				var fos = new FileStream(path, FileMode.OpenOrCreate);
				_flvOutput = fos;
			}
			catch (IOException e)
			{
				logger.Error("create file failed", e);
			}
		}

		public async Task AddSubscriber(IChannel channel)
		{
			logger.Info($"subscriber : {channel.RemoteAddress} is added to stream :{_streamName}");
			_subscribers.GetOrAdd(channel.RemoteAddress.ToString(), p => channel);
			_avcDecoderConfigurationRecord.Timestamp = _content[0].Timestamp;
			await channel.WriteAndFlushAsync(_avcDecoderConfigurationRecord);

			foreach (var msg in _content)
			{
				await channel.WriteAndFlushAsync(msg);
			}
		}

		public async Task AddHttpFlvSubscriber(IChannel channel)
		{	try
			{
				logger.Info($"http flv subscriber : {channel.RemoteAddress} is added to stream :{_streamName}");
				_httpFLvSubscribers.GetOrAdd(channel.RemoteAddress.ToString(), p => channel); ;
				Memory<byte> meta = EncodeFlvHeaderAndMetadata();
				await channel.WriteAndFlushAsync(Unpooled.WrappedBuffer(meta.ToArray()));

				_avcDecoderConfigurationRecord.Timestamp = _content[0].Timestamp;
				Memory<byte> config = EncodeMediaAsFlvTagAndPrevTagSize(_avcDecoderConfigurationRecord);
				await channel.WriteAndFlushAsync(Unpooled.WrappedBuffer(config.ToArray()));
				if (_aacAudioSpecificConfig != null)
				{
					_aacAudioSpecificConfig.Timestamp = _content[0].Timestamp;
					Memory<byte> aac = EncodeMediaAsFlvTagAndPrevTagSize(_aacAudioSpecificConfig);
					await channel.WriteAndFlushAsync(Unpooled.WrappedBuffer(aac.ToArray()));
				}

				var content1 = new List<AbstractRtmpMediaMessage>(_content);

				foreach (var msg in content1)
				{
					if (channel.IsActive && channel.IsWritable)
						await channel.WriteAndFlushAsync(Unpooled.WrappedBuffer(EncodeMediaAsFlvTagAndPrevTagSize(msg).ToArray()));
				}
			}
			catch
			{

			}

		}

		public async void BroadCastToSubscribers(AbstractRtmpMediaMessage msg)
		{
			foreach (var item in _subscribers)
			{
				if (item.Value.IsActive)
				{
					await item.Value.WriteAndFlushAsync(msg);
				}
				else
				{
					_subscribers.Remove(item.Key, out IChannel channel);
					if (channel != null)
						await channel.CloseAsync();
				}
			}

			if (_httpFLvSubscribers.Count > 0)
			{
                Memory<byte> encoded = EncodeMediaAsFlvTagAndPrevTagSize(msg);

				foreach (var item in _httpFLvSubscribers)
				{
					try
					{
						var wrappedBuffer = Unpooled.WrappedBuffer(encoded.ToArray());
						if (item.Value.IsActive)
						{
							await item.Value.WriteAndFlushAsync(wrappedBuffer);
						}
						else
						{
							_httpFLvSubscribers.Remove(item.Key, out IChannel channel);
                            if (channel != null) await channel.CloseAsync();

                        }
					}
					catch 
					{
                        _httpFLvSubscribers.Remove(item.Key, out IChannel channel);
                         if(channel!=null) 
							await channel.CloseAsync();
                    }

				}
			}
		}

		public async Task SendEofToAllSubscriberAndClose()
		{
			try
			{
				if (RtmpConfig.Instance!=null && RtmpConfig.Instance.IsSaveFlvFile &&  _flvOutput != null)
				{
					try
					{
						_flvOutput.Close();
					}
					catch (IOException e)
					{
						_flvOutput.Close();
						logger.Error("close file  failed", _flvOutput);
					}
				}
				foreach (var subscriber in _subscribers)
				{
					await subscriber.Value.WriteAndFlushAsync(UserControlMessageEvent.StreamEOF(Constants.DEFAULT_STREAM_ID));
				}

				foreach (var subscriber in _httpFLvSubscribers)
				{
					var content = new DefaultLastHttpContent();
					await subscriber.Value.WriteAndFlushAsync(EmptyLastHttpContent.Default);
				}
			}
			catch
			{

			}
		}
	}
}
