#region License
//  The MIT License (MIT)
//
//  Copyright (c) 2017, Cagatay Dogan
//
//  Permission is hereby granted, free of charge, to any person obtaining a copy
//  of this software and associated documentation files (the "Software"), to deal
//  in the Software without restriction, including without limitation the rights
//  to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
//  copies of the Software, and to permit persons to whom the Software is
//  furnished to do so, subject to the following conditions:
//
//      The above copyright notice and this permission notice shall be included in
//      all copies or substantial portions of the Software.
//
//      THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
//      IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
//      FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
//      AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
//      LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
//      OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
//      THE SOFTWARE.
#endregion License

using System;
using System.Collections.Generic;
using System.Threading;

namespace Sweet.Actors
{
    internal class ReceiveBuffer : Disposable
    {
        private struct ReceivedHeader
        {
            private int _processId;
            private int _messageId;
            private ushort _frameCount;

            public int ProcessId { get => _processId; set => _processId = value; }

            public int MessageId { get => _messageId; set => _messageId = value; }

            public ushort FrameCount { get => _frameCount; set => _frameCount = value; }
        }

        private struct ReceivedFrame
        {
            private int _processId;
            private int _messageId;
            private ushort _frameId;
            private byte[] _frameData;

            public int ProcessId { get => _processId; set => _processId = value; }

            public int MessageId { get => _messageId; set => _messageId = value; }

            public ushort FrameId { get => _frameId; set => _frameId = value; }

            public byte[] FrameData { get => _frameData; set => _frameData = value; }
        }

        private class ReceivedMessage
        {
            public ReceivedHeader Header { get; }
            public IList<ReceivedFrame> Frames { get; } = new List<ReceivedFrame>();

            public ReceivedMessage() { }
        }

        private int _segmentSize;
        private BufferCache _cache;
        private BuffserSegmentReader _reader;
        private IList<BufferSegment> _segments = new List<BufferSegment>();

		public ReceiveBuffer()
        {
            _cache = BufferCache.Default;
            _segmentSize = _cache.SegmentSize;
            _reader = new BuffserSegmentReader(_segments, _cache);
        }

		public void Reset()
		{
            Reset(true);
        }

        private void Reset(bool reInit)
        {
            var newSegments = reInit ? new List<BufferSegment>() : null;
            var newReader = reInit ? new BuffserSegmentReader(newSegments, _cache) : null;

            var segments = Interlocked.Exchange(ref _segments, newSegments);
            var reader = Interlocked.Exchange(ref _reader, newReader);

            Release(segments);
            using (reader) { }
        }

        public void OnReceiveData(byte[] buffer, int bytesTransferred)
		{
            if ((buffer != null) && (bytesTransferred > 0))
            {
                var offset = 0;
                var requiredSegmentCnt = 1 + (bytesTransferred / _segmentSize);

                var acquiredSegments = _cache.Acquire(requiredSegmentCnt);

                var segmentCnt = acquiredSegments?.Count ?? 0;
                for (var i = 0; i < segmentCnt; i++)
                {
                    var written = acquiredSegments[i].Write(buffer, offset, bytesTransferred);

                    offset += written;
                    bytesTransferred -= written;

                    if (bytesTransferred <= 0)
                        break;
                }
            }
        }

		internal bool TryDecodeMessage(out Message msg)
		{
			msg = null;
            var segments = _segments;

            var segmentCnt = segments?.Count ?? 0;
            if (segmentCnt > 0)
            {
                var offset = 0;

                var headSegment = segments[0];

                var available = headSegment.Available;
                if (headSegment.Buffer[offset++] != Constants.HeaderSign)
                    throw new Exception(Errors.InvalidMessageType);

                var receivedMsg = new ReceivedMessage();

                for (var i = 0; i < segmentCnt; i++)
                {
                    var segment = segments[i];
                    if (segment.Available > 0)
                    {

                    }
                }
            }
			return false;
        }

        protected override void OnDispose(bool disposing)
        {
            Reset(false);
            _cache = null;
        }

        private void Release(IList<BufferSegment> segments)
        {
            if (segments != null && segments.Count > 0)
            {
                var cache = _cache ?? BufferCache.Default;
                foreach (var segment in segments)
                    cache.Release(segment);

                segments.Clear();
            }
        }
    }
}
