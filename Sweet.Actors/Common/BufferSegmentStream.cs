using System;
using System.IO;

namespace Sweet.Actors
{

    internal class BufferSegmentStream : Stream
    {
        private class SegmentNode
        {
            public SegmentNode Next;
            public BufferSegment Segment;
        }

        private SegmentNode _head;
        private BufferCache _cache;

        private bool _isClosed;

        private int _writeOffset;
        private SegmentNode _writeNode;

        private int _readOffset;
        private SegmentNode _readNode;

        public BufferSegmentStream(BufferCache cache)
        {
            _cache = cache;
        }

        public override bool CanRead => !_isClosed;

        public override bool CanSeek => !_isClosed;

        public override bool CanWrite => !_isClosed;

        public override long Length
        {
            get
            {
                ThrowIfClosed();

                var length = 0;

                var node = _head;
                while (node != null)
                {
                    var next = node.Next;
                    if (next != null)
                        length += node.Segment.Capacity;
                    else
                        length += _writeOffset;

                    node = next;
                }

                return length;
            }
        }

        public override long Position
        {
            get
            {
                ThrowIfClosed();

                if (_readNode == null)
                    return 0;

                var pos = 0;
                var node = _head;
                while (node != _readNode)
                {
                    pos += node.Segment.Capacity;
                    node = node.Next;
                }
                pos += _readOffset;

                return pos;
            }
            set
            {
                ThrowIfClosed();

                if (value < 0)
                    throw new ArgumentOutOfRangeException(nameof(value));

                var prevReadNode = _readNode;
                var prevReadOffset = _readOffset;

                _readNode = null;
                _readOffset = 0;

                var iValue = (int)value;

                var node = _head;
                while (node != null)
                {
                    var segmentCap = node.Segment.Capacity;

                    if ((iValue < segmentCap) || 
                        ((iValue == segmentCap) && (node.Next == null)))
                    {
                        _readNode = node;
                        _readOffset = iValue;

                        break;
                    }

                    node = node.Next;
                    iValue -= segmentCap;
                }

                if (_readNode == null)
                {
                    _readNode = prevReadNode;
                    _readOffset = prevReadOffset;

                    throw new ArgumentOutOfRangeException(nameof(value));
                }
            }
        }

        private void ThrowIfClosed()
        {
            if (_isClosed)
                throw new Exception(Errors.StreamIsClosed);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            ThrowIfClosed();

            switch (origin)
            {
                case SeekOrigin.Begin:
                    Position = offset;
                    break;
                case SeekOrigin.Current:
                    Position += offset;
                    break;
                case SeekOrigin.End:
                    Position = Length + offset;
                    break;
            }

            return Position;
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        protected override void Dispose(bool disposing)
        {
            try
            {
                _isClosed = true;
                if (disposing)
                    ReleaseNodes(_head);

                _head = null;
                _writeNode = null;
                _readNode = null;
            }
            finally
            {
                base.Dispose(disposing);
            }
        }

        public override void Flush()
        { }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ThrowIfClosed();

            if (_readNode == null)
            {
                if (_head == null)
                    return 0;

                _readNode = _head;
                _readOffset = 0;
            }

            var segment = _readNode.Segment;
            var segmentCap = segment.Capacity;

            if (_readNode.Next == null)
                segmentCap = _writeOffset;

            var bytesRead = 0;
            while (count > 0)
            {
                if (_readOffset == segmentCap)
                {
                    if (_readNode.Next == null)
                        break;

                    _readNode = _readNode.Next;
                    _readOffset = 0;

                    segment = _readNode.Segment;
                    segmentCap = segment.Capacity;

                    if (_readNode.Next == null)
                        segmentCap = _writeOffset;
                }

                var readCount = Math.Min(count, segmentCap - _readOffset);
                Buffer.BlockCopy(segment.Buffer, _readOffset, buffer, offset, readCount);

                offset += readCount;
                count -= readCount;
                bytesRead += readCount;

                _readOffset += readCount;
            }

            return bytesRead;
        }

        public override int ReadByte()
        {
            ThrowIfClosed();

            if (_readNode == null)
            {
                if (_head == null)
                    return 0;

                _readNode = _head;
                _readOffset = 0;
            }

            var segment = _readNode.Segment;
            var segmentCap = segment.Capacity;

            if (_readNode.Next == null)
                segmentCap = _writeOffset;

            if (_readOffset == segmentCap)
            {
                if (_readNode.Next == null)
                    return -1;

                _readNode = _readNode.Next;
                _readOffset = 0;

                segment = _readNode.Segment;
                segmentCap = segment.Capacity;

                if (_readNode.Next == null)
                    segmentCap = _writeOffset;
            }

            return segment.Buffer[_readOffset++];
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            ThrowIfClosed();

            if (_head == null)
            {
                _head = AcquireNode();

                _writeNode = _head;
                _writeOffset = 0;
            }

            var segment = _writeNode.Segment;
            var segmentCap = segment.Capacity;

            while (count > 0)
            {
                if (_writeOffset == segmentCap)
                {
                    _writeNode.Next = AcquireNode();

                    _writeNode = _writeNode.Next;
                    _writeOffset = 0;

                    segment = _writeNode.Segment;
                    segmentCap = segment.Capacity;
                }

                var copyCount = Math.Min(count, segmentCap - _writeOffset);
                Buffer.BlockCopy(buffer, offset, segment.Buffer, _writeOffset, copyCount);

                offset += copyCount;
                count -= copyCount;
                _writeOffset += copyCount;
            }
        }

        public override void WriteByte(byte value)
        {
            ThrowIfClosed();

            if (_head == null)
            {
                _head = AcquireNode();

                _writeNode = _head;
                _writeOffset = 0;
            }

            var segment = _writeNode.Segment;
            var segmentCap = segment.Capacity;

            if (_writeOffset == segmentCap)
            {
                _writeNode.Next = AcquireNode();

                _writeNode = _writeNode.Next;
                _writeOffset = 0;

                segment = _writeNode.Segment;
                segmentCap = segment.Capacity;
            }

            segment.Buffer[_writeOffset++] = value;
        }

        public virtual byte[] ToArray()
        {
            var length = (int)Length;
            var result = new byte[length];

            var prevReadNode = _readNode;
            var prevReadOffset = _readOffset;

            _readNode = _head;
            _readOffset = 0;

            Read(result, 0, length);

            _readNode = prevReadNode;
            _readOffset = prevReadOffset;

            return result;
        }

        public virtual void WriteTo(Stream stream)
        {
            ThrowIfClosed();

            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            if (_readNode == null)
            {
                if (_head == null)
                    return;

                _readNode = _head;
                _readOffset = 0;
            }

            var segment = _readNode.Segment;
            var segmentCap = segment.Capacity;

            if (_readNode.Next == null)
                segmentCap = _writeOffset;

            while (true)
            {
                if (_readOffset == segmentCap)
                {
                    if (_readNode.Next == null)
                        break;

                    _readNode = _readNode.Next;
                    _readOffset = 0;

                    segment = _readNode.Segment;
                    segmentCap = segment.Capacity;

                    if (_readNode.Next == null)
                        segmentCap = _writeOffset;
                }

                var writeCount = segmentCap - _readOffset;

                stream.Write(segment.Buffer, _readOffset, writeCount);
                _readOffset = segmentCap;
            }

        }

        private SegmentNode AcquireNode()
        {
            return new SegmentNode {
                Segment = _cache.Acquire(),
                Next = null
            };
        }

        private void ReleaseNodes(SegmentNode head)
        {
            while (head != null)
            {
                _cache.Release(head.Segment);
                head = head.Next;
            }
        }
    }
}