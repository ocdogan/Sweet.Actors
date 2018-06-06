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

namespace Sweet.Actors
{
	public class BufferSegment : IDisposable
    {
        private int _capacity;
        private int _length;

        private byte[] _buffer;

        internal BufferSegment(int capacity)
		{
			_capacity = Math.Max(0, capacity);
			_buffer = new byte[_capacity];
		}

        internal BufferSegment(byte[] buffer, int length = -1)
        {
            _buffer = buffer;
            _capacity = buffer?.Length ?? 0;
            _length = (length > -1) ? Math.Min(length, _capacity) : _capacity;
        }

        public int Available => _capacity - _length;

        public int Capacity => _capacity;

        public int Length => _length;

        public byte[] Buffer => _buffer;

        public int Write(byte value)
        {
            if (_buffer == null)
                return 0;

            var appendLen = Math.Min(1, Math.Max(0, _capacity - _length));
            if (appendLen > 0)
                _buffer[_length++] = value;

            return appendLen;
        }

        public int Write(byte[] data)
		{
			if (data != null)
                return Write(data, _length, data.Length);
			return 0;
		}

        public int Write(byte[] data, int offset, int length)
        {
            if (offset >= 0)
            {
                if (length <= 0 || _buffer == null)
                    return 0;

                var dataLen = data?.Length ?? 0;
                if (dataLen == 0 || dataLen <= offset)
                    return 0;

                var appendLen = Math.Min(dataLen, Math.Max(0, _capacity - _length));
                if (appendLen > 0)
                {
                    Array.Copy(data, offset, _buffer, _length, appendLen);
                    _length += appendLen;
                }

                return appendLen;
            }
            return -1;
        }

        public void Reset()
		{
			_length = 0;
		}
        
		public void Dispose()
		{
			_capacity = 0;
			_length = 0;
			_buffer = null;
		}
	}
}
