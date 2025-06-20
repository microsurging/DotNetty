﻿using DotNetty.Buffers;
using DotNetty.Codecs.Rtmp.AMF;
using DotNetty.Codecs.Rtmp.Messages;
using DotNetty.Common.Utilities;
using DotNetty.Transport.Channels;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;

namespace DotNetty.Codecs.Rtmp.Handlers
{
	public class ChunkDecoder : ReplayingDecoder<DecodeState>
	{
		private int _clientChunkSize = 128;
		Dictionary<int, RtmpHeader> prevousHeaders = new  Dictionary<int, RtmpHeader>(4);
		Dictionary<int, IByteBuffer> inCompletePayload = new Dictionary<int, IByteBuffer>(4);

		private IByteBuffer _currentPayload = null;
		private int _currentCsid;

		private int _ackWindowSize = -1;

		public ChunkDecoder() : base(DecodeState.STATE_HEADER) { }

		public ChunkDecoder(DecodeState state) : base(state) { }

        public override void ChannelInactive(IChannelHandlerContext ctx)
        {
			try
			{
				base.ChannelInactive(ctx);
			}
			finally
			{
				prevousHeaders.Clear();
				inCompletePayload.Clear();
				_currentPayload = null;
			}
        }
       
        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception exception)
		{
		}
		protected override void Decode(IChannelHandlerContext context, IByteBuffer input, List<object> output)
		{ 
			var state = State; 
			if(state == DecodeState.NONE)
			{
				Checkpoint(DecodeState.STATE_HEADER);
			}
			else if (state == DecodeState.STATE_HEADER)
			{
				RtmpHeader rtmpHeader = ReadHeader(input);

				completeHeader(rtmpHeader);
				_currentCsid = rtmpHeader.Csid;

				// initialize the payload
				if (rtmpHeader.Fmt != Constants.CHUNK_FMT_3)
				{
					IByteBuffer buffer = Unpooled.Buffer(rtmpHeader.MessageLength, rtmpHeader.MessageLength);
					inCompletePayload.Remove(rtmpHeader.Csid);
					prevousHeaders.Remove(rtmpHeader.Csid);
					inCompletePayload.Add(rtmpHeader.Csid, buffer);
					prevousHeaders.Add(rtmpHeader.Csid,  rtmpHeader);
				}

				_currentPayload = inCompletePayload.GetValueOrDefault(rtmpHeader.Csid);
				if (_currentPayload == null)
				{
					RtmpHeader previousHeader = prevousHeaders.GetValueOrDefault(rtmpHeader.Csid);
					_currentPayload = Unpooled.Buffer(previousHeader.MessageLength, previousHeader.MessageLength);
					inCompletePayload.Add(rtmpHeader.Csid,_currentPayload);
				}

				Checkpoint(DecodeState.STATE_PAYLOAD);
			}
			else if (state == DecodeState.STATE_PAYLOAD)
			{
				byte[] bytes = new byte[Math.Min(_currentPayload.WritableBytes, _clientChunkSize)];
				input.ReadBytes(bytes);
				_currentPayload.WriteBytes(bytes);
				Checkpoint(DecodeState.STATE_HEADER);

				if (_currentPayload.IsWritable())
				{  
					return;
				}
				inCompletePayload.Remove(_currentCsid,out IByteBuffer byteBuffer);
				 
				IByteBuffer payload = _currentPayload;
				RtmpHeader header = prevousHeaders.GetValueOrDefault(_currentCsid);

				var msg = RtmpMessageDecoder.Decode(header, payload);
				if (msg == null)
				{
					base.HandlerRemoved(context);
					return;
				}

				if (msg is SetChunkSize)
				{ 
					var scs = (SetChunkSize)msg;
					_clientChunkSize = scs.ChunkSize;

				}
				else
				{
					output.Add(msg);
				}
			}
		}

		private RtmpHeader ReadHeader(IByteBuffer input)
		{
			RtmpHeader rtmpHeader = new RtmpHeader();

			// alway from the beginning
			int headerLength = 0;

			byte firstByte = input.ReadByte();
			headerLength += 1;

			// CHUNK HEADER is divided into
			// BASIC HEADER
			// MESSAGE HEADER
			// EXTENDED TIMESTAMP

			// BASIC HEADER
			// fmt and chunk steam id in first byte
			int fmt = (firstByte & 0xff) >> 6;
			int csid = (firstByte & 0x3f);

			if (csid == 0)
			{
				// 2 byte form
				csid = input.ReadByte() & 0xff + 64;
				headerLength += 1;
			}
			else if (csid == 1)
			{
				// 3 byte form
				byte secondByte = input.ReadByte();
				byte thirdByte = input.ReadByte();
				csid = (thirdByte & 0xff) << 8 + (secondByte & 0xff) + 64;
				headerLength += 2;
			}
			else if (csid >= 2)
			{
				// that's it!
			}

			rtmpHeader.Csid = csid;
			rtmpHeader.Fmt = fmt;

			// basic header complete

			// MESSAGE HEADER
			switch (fmt)
			{
				case Constants.CHUNK_FMT_0:
					{
						int timestamp = input.ReadMedium();
						int messageLength = input.ReadMedium();
						short messageTypeId = (short)(input.ReadByte() & 0xff);
						int messageStreamId = input.ReadIntLE();
						headerLength += 11;
						if (timestamp == Constants.MAX_TIMESTAMP)
						{
							long extendedTimestamp = input.ReadInt();
							rtmpHeader.ExtendedTimestamp = extendedTimestamp;
							headerLength += 4;
						}

						rtmpHeader.Timestamp = timestamp;
						rtmpHeader.MessageTypeId = messageTypeId;
						rtmpHeader.MessageStreamId = messageStreamId;
						rtmpHeader.MessageLength = messageLength;

					}
					break;
				case Constants.CHUNK_FMT_1:
					{
						int timestampDelta = input.ReadMedium();
						int messageLength = input.ReadMedium();
						short messageType = (short)(input.ReadByte() & 0xff);

						headerLength += 7;
						if (timestampDelta == Constants.MAX_TIMESTAMP)
						{
							long extendedTimestamp = input.ReadInt();
							rtmpHeader.ExtendedTimestamp = extendedTimestamp;
							headerLength += 4;
						}

						rtmpHeader.TimestampDelta = timestampDelta;
						rtmpHeader.MessageLength = messageLength;
						rtmpHeader.MessageTypeId = messageType;
					}
					break;
				case Constants.CHUNK_FMT_2:
					{
						int timestampDelta = input.ReadMedium();
						headerLength += 3;
						rtmpHeader.TimestampDelta = timestampDelta;

						if (timestampDelta == Constants.MAX_TIMESTAMP)
						{
							long extendedTimestamp = input.ReadInt();
							rtmpHeader.ExtendedTimestamp = extendedTimestamp;
							headerLength += 4;
						}

					}
					break;

				case Constants.CHUNK_FMT_3:
					break;

				default:
					throw new ArgumentException("illegal fmt type:" + fmt);

			}

			rtmpHeader.HeaderLength = headerLength;

			return rtmpHeader;
		}


		private void completeHeader(RtmpHeader rtmpHeader)
		{
			 prevousHeaders.TryGetValue(rtmpHeader.Csid,out RtmpHeader prev);
			if (prev == null)
			{
				return;
			}
			switch (rtmpHeader.Fmt)
			{
				case Constants.CHUNK_FMT_1:
					rtmpHeader.MessageStreamId = prev.MessageStreamId;
					break;
				case Constants.CHUNK_FMT_2:
					rtmpHeader.MessageLength = prev.MessageLength;
					rtmpHeader.MessageStreamId = prev.MessageStreamId;
					rtmpHeader.MessageTypeId = prev.MessageTypeId;
					break;
				case Constants.CHUNK_FMT_3:
					rtmpHeader.MessageStreamId = prev.MessageStreamId;
					rtmpHeader.MessageTypeId = prev.MessageTypeId;
					rtmpHeader.Timestamp = prev.Timestamp;
					rtmpHeader.TimestampDelta = prev.TimestampDelta;
					break;
				default:
					break;
			}

		}

	}
}
