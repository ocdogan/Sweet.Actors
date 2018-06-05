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

namespace Sweet.Actors
{
    public class BuffserSegmentReader : Disposable
    {
        private static readonly byte[] EmptyBytes = new byte[0];
        private static readonly IList<BufferSegment> EmptySegments = new List<BufferSegment>();

        private int _index;
        private int _offset;

        private IList<BufferSegment> _segments;

        public BuffserSegmentReader(IList<BufferSegment> segments)
        {
            _segments = segments;
        }

        protected override void OnDispose(bool disposing)
        {
            _segments = EmptySegments;
        }

        public void SetOffset(int offset)
        {
            var index = -1;
            offset = Math.Max(0, offset);
            try
            {
                if (offset > 0)
                {
                    var count = _segments.Count;
                    if (count == 0)
                        offset = -1;
                    else
                    {
                        for (index = 0; index < count; index++)
                        {
                            var segment = _segments[index];

                            var segmentLen = segment.Length;
                            if (segmentLen > 0)
                            {
                                offset -= segmentLen;
                                if (offset <= 0)
                                    break;
                            }
                        }

                        if (offset > 0)
                            offset = _segments[count-1].Length;
                    }
                }
            }
            finally
            {
                _index = index;
                _offset = offset;
            }
        }

        private int CalculateLength()
        {
            var result = 0;

            var count = _segments?.Count ?? 0;
            for (var i = 0; i < count; i++)
                result += _segments[i]?.Length ?? 0;

            return result;
        }

        public byte[] ReadBytes(int length)
        {
            if (length < 0)
                return null;

            var size = CalculateLength();
            if (size - _offset < length)
                return null;

            var count = _segments?.Count ?? 0;
            if (count > 0)
            {
                var cursor = 0;
                var result = new byte[length];

                for (var i = _index; i < count; i++)
                {
                    var segment = _segments[i];

                    var segmentLen = segment?.Length ?? 0;
                    if (segmentLen > 0)
                    {
                        var copyLen = Math.Min((segmentLen - _offset), length);

                        Array.Copy(segment.Buffer, _offset, result, cursor, copyLen);

                        length -= copyLen;
                        cursor += copyLen;

                        _offset += copyLen;
                        if (_offset >= segmentLen)
                        {
                            _index++;
                            _offset = 0;
                        }
                    }
                }
                return result;
            }
            return EmptyBytes;
        }
    }
}
