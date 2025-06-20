using MessagePack;
using System;
using System.Collections.Generic;
using System.Text;

namespace DotNetty.Codecs.Rtmp.Messages
{
    [MessagePackObject]
    public abstract class AbstractRtmpMediaMessage : AbstractRtmpMessage
    {
        [Key(3)]
        public int? TimestampDelta { get; set; }
        [Key(4)]
        public int? Timestamp { get; set; }
        public abstract byte[] Raw();
    }
}
