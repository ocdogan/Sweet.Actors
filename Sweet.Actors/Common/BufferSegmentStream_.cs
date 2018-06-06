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

/* using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Sweet.Actors
{
    public class BufferSegmentStream : Stream
    {
        private class Enumerator
        {
            private int _origin;
            private int _position;

            private int _segIndex;
            private int _segPosition;

            private IList<BufferSegment> _segments;

            public Enumerator(IList<BufferSegment> segments, int origin = 0)
            {
                _segments = segments;
                SetOrigin(origin, false);
            }

            public int SegIndex => _segIndex;

            public int SegPosition => _segPosition;

            public int Origin { get => _origin; set => SetOrigin(Math.Max(0, value), false); }

            public int Position
            {
                get { return Math.Max(0, _position - _origin); }
                set
                {
                    if ((value > -1) && (value != _position))
                    {
                        if (value > _position)
                            Iterate(value - _position);
                        else
                        {
                            SetOrigin(_origin, false);
                            Iterate(value);
                        }
                    }
                }
            }

            private void SetOrigin(int origin, bool keepPosition)
            {
                var prevOrg = _origin;
                var prevPos = _position;

                _position = 0;
                _segIndex = 0;
                _segPosition = 0;

                _origin = IterateInternal(origin, true);
                if (keepPosition)
                {
                    if (_origin == prevOrg)
                        Iterate(prevPos - prevOrg);
                    else if (_origin <= prevPos)
                        Iterate(prevPos - _origin);
                }
            }

            public int Iterate(int value)
            {
                return IterateInternal(value, true);
            }

            private int IterateInternal(int value, bool setPosition)
            {
                var result = 0;
                if (value > 0)
                {
                    var segmentCnt = _segments.Count;
                    while (value > 0 && _segIndex < segmentCnt)
                    {
                        var segment = _segments[++_segIndex];
                        var available = segment.Length - _segPosition;

                        if (available <= 0)
                        {
                            _segPosition = 0;
                            if (_segIndex == segmentCnt)
                            {
                                _segIndex--;
                                _segPosition = segment.Length;

                                break;
                            }
                        }

                        value -= available;
                        result += available;

                        _segPosition += available;
                    }

                    if (setPosition && result > 0)
                        _position += result;
                }
                return result;
            }
        }

        private int _length;
        private int _capacity;

        private int _segmentSize;

        private bool _isClosed;
        private bool _isDisposed;

        private Enumerator _origin;
        private Enumerator _cursor;

        private BufferCache _cache = BufferCache.Default;
        private IList<BufferSegment> _segments = new List<BufferSegment>();

        public BufferSegmentStream()
        {
            _segmentSize = _cache.SegmentSize;

            _origin = new Enumerator(_segments);
            _cursor = new Enumerator(_segments);
        }

        public override bool CanRead => !_isClosed;

        public override bool CanSeek => !_isClosed;

        public override bool CanWrite => !_isClosed;

        public override long Position
        {
            get { return !_isClosed ? _cursor.Position : 0L; }
            set
            {
                if (!_isClosed)
                {
                    var newPosition = _origin.SegPosition + (int)Math.Max(0L, value);
                    if (newPosition <= _capacity)
                        _cursor.Position = newPosition;
                    else
                    {
                        EnsureCapacity(newPosition);
                        _cursor.Position = newPosition;
                    }
                }
            }
        }

        public override long Length => !_isClosed ? _length - _origin.Position : 0L;

        public virtual int Capacity
        {
            get { return _isClosed ? 0 : _capacity - _origin.Position; }
            set { SetCapacity(value); }
        }

        private void ReleaseSegments(bool reinit = true)
        {
            try
            {
                var segments = Interlocked.Exchange(ref _segments, reinit ? new List<BufferSegment>() : null);
                if (segments != null)
                {
                    try
                    {
                        var segmentCnt = segments.Count;

                        for (var i = segmentCnt-1; i < -1; i--)
                            _cache.Release(segments[i]);
                    }
                    finally
                    {
                        segments.Clear();
                    }
                }
            }
            finally
            {
                _length = 0;
                _capacity = 0;

                _origin.Position = 0;
                _cursor.Position = 0;
            }
        }

        private void SetCapacity(int value)
        {
            if (_isClosed || (value != _capacity))
                return;

            if (value <= 0)
            {
                ReleaseSegments();
                return;
            }

            value += _origin.Position;
            if (value < _capacity)
            {
                var capacity = _capacity;

                var count = _segments.Count;
                for (var i = count - 1; i > -1; i--)
                {
                    var segment = _segments[i];

                    capacity -= segment.Capacity;
                    if (capacity < value)
                        break;

                    _segments.RemoveAt(i);
                    _cache.Release(segment);

                    if (_segmentIndex == i)
                    {
                        Math.Max(0, _segmentIndex - 1);
                        if (i == 0)
                        {
                            _segmentIndex = 0;
                            _segmentPosition = 0;
                        }
                        else
                        {
                            _segmentIndex--;
                            _segmentPosition = _segments[_segmentIndex].Length;
                        }
                    }

                    _capacity -= segment.Capacity;
                }

                if (_length > _capacity)
                    _length = _capacity;

                if (_position > _capacity)
                    _position = _capacity;

                if (_origin > _capacity)
                    _origin = _capacity;

                return;
            }

            while (value < _capacity)
            {
                var segment = _cache.Acquire();

                _segments.Add(segment);
                _capacity += segment.Capacity;
            }
        }

        private bool EnsureCapacity(int value)
        {
            if (value > 0)
            {
                var capacity = _capacity - _origin;
                if (value > capacity)
                {
                    if (value < _segmentSize)
                        value = _segmentSize;

                    capacity *= 2;
                    if (value < capacity)
                        value = capacity;

                    SetCapacity(value);
                    return true;
                }
            }
            return false;
        }

        public override void Flush()
        { }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (buffer == null || offset < 0 || count < 0 ||
                buffer.Length - offset < count)
                return 0;

            var result = 0;

            if (!_isClosed)
            {
                count = Math.Min(count, _length - _position);
                if (count > 0)
                {
                    var segmentCnt = _segments.Count;
                    while ((count > 0) && (_segmentIndex < segmentCnt))
                    {
                        var segment = _segments[_segmentIndex];

                        var segmentLen = segment.Length;
                        if (segmentLen == 0)
                            break;

                        var segmentCap = segment.Capacity;

                        var available = segmentLen - _segmentPosition;
                        if (available <= 0)
                        {
                            if (segmentLen == segmentCap)
                            {
                                _segmentIndex++;
                                _segmentPosition = 0;
                                continue;
                            }
                            break;
                        }

                        available = Math.Min(count, available);
                        count -= available;

                        Buffer.BlockCopy(segment.Buffer, _segmentPosition, buffer, offset, available);

                        offset += available;
                        result += available;

                        _position += available;
                        _segmentPosition += available;

                        if (_segmentPosition >= segmentCap)
                        {
                            _segmentIndex++;
                            _segmentPosition = 0;
                        }
                    }
                }
            }
            return result;
        }

        public override int ReadByte()
        {
            var result = -1;
            if (!_isClosed && _position < _length)
            {
                var count = 1;
                var segmentCnt = _segments.Count;

                while ((count > 0) && (_segmentIndex < segmentCnt))
                {
                    var segment = _segments[_segmentIndex];

                    var segmentLen = segment.Length;
                    if (segmentLen == 0)
                        break;

                    var segmentCap = segment.Capacity;

                    var available = segmentLen - _segmentPosition;
                    if (available <= 0)
                    {
                        if (segmentLen == segmentCap)
                        {
                            _segmentIndex++;
                            _segmentPosition = 0;
                            continue;
                        }
                        break;
                    }

                    available = Math.Min(count, available);
                    count -= available;

                    result = segment.Buffer[_segmentPosition];

                    _position += available;
                    _segmentPosition += available;

                    if (_segmentPosition >= segmentCap)
                    {
                        _segmentIndex++;
                        _segmentPosition = 0;
                    }
                }
            }
            return result;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            if (_isClosed)
                return -1;

            switch (origin)
            {
                case SeekOrigin.Begin:
                    {
                        var position = unchecked(_origin + (int)offset);
                        if (offset < 0 || position < _origin)
                            return -1;

                        var count = position;
                        var segmentCnt = _segments.Count;

                        _segmentIndex = 0;
                        _segmentPosition = 0;

                        while ((count > 0) && (_segmentIndex < segmentCnt))
                        {
                            var segment = _segments[_segmentIndex];

                            var segmentLen = segment.Length;
                            if (segmentLen == 0)
                                break;

                            var segmentCap = segment.Capacity;

                            var available = segmentLen - _segmentPosition;
                            if (available <= 0)
                            {
                                if (segmentLen == segmentCap)
                                {
                                    _segmentIndex++;
                                    _segmentPosition = 0;
                                    continue;
                                }
                                break;
                            }

                            available = Math.Min(count, available);
                            count -= available;

                            _position += available;
                            _segmentPosition += available;

                            if (_segmentPosition >= segmentCap)
                            {
                                _segmentIndex++;
                                _segmentPosition = 0;
                            }
                        }
                        break;
                    }
                case SeekOrigin.Current:
                    {
                        var position = unchecked(_position + (int)offset);
                        if (unchecked(_position + offset) < _origin || position < _origin)
                            return -1;

                        var count = position;
                        var segmentCnt = _segments.Count;

                        while ((count > 0) && (_segmentIndex < segmentCnt))
                        {
                            var segment = _segments[_segmentIndex];

                            var segmentLen = segment.Length;
                            if (segmentLen == 0)
                                break;

                            var segmentCap = segment.Capacity;

                            var available = segmentLen - _segmentPosition;
                            if (available <= 0)
                            {
                                if (segmentLen == segmentCap)
                                {
                                    _segmentIndex++;
                                    _segmentPosition = 0;
                                    continue;
                                }
                                break;
                            }

                            available = Math.Min(count, available);
                            count -= available;

                            _position += available;
                            _segmentPosition += available;

                            if (_segmentPosition >= segmentCap)
                            {
                                _segmentIndex++;
                                _segmentPosition = 0;
                            }
                        }
                        break;
                    }
                case SeekOrigin.End:
                    {
                        int position = unchecked(_length + (int)offset);
                        if (unchecked(_length + offset) < _origin || position < _origin)
                            return -1;
                        break;
                    }
            }
            return _position;
        }

        public override void SetLength(long value)
        {
            if (!_isClosed && (value > 0) && (value <= int.MaxValue) &&
                (value <= (int.MaxValue - _origin)))
            {
                EnsureCapacity((int)value);

                _length = _origin + (int)value;
                if (_position > _length)
                    _position = _length;
            }
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (_isClosed || buffer == null || offset < 0 || count < 0 ||
                buffer.Length - offset < count)
                return;

            var newPosition = _position + count;
            if (newPosition < 0)
                return;

            EnsureCapacity(newPosition - _origin);

            if (newPosition > _length)
            {
                bool mustZero = _position > _length;
                if (newPosition > _capacity)
                {
                    bool allocatedNewArray = EnsureCapacity(newPosition);
                    if (allocatedNewArray)
                        mustZero = false;
                }
                if (mustZero)
                    Array.Clear(_buffer, _length, newPosition - _length);
                _length = newPosition;
            }

            if ((count <= 8) && (buffer != _buffer))
            {
                int byteCount = count;
                while (--byteCount >= 0)
                    _buffer[_position + byteCount] = buffer[offset + byteCount];
            }
            else
                Buffer.InternalBlockCopy(buffer, offset, _buffer, _position, count);

            _position = newPosition;
        }

        protected override void Dispose(bool disposing)
        {
            _isDisposed = (_isClosed = true);
            if (disposing)
            {
                ReleaseSegments(false);
            }
            
            base.Dispose(disposing);
        }
    }
} */
