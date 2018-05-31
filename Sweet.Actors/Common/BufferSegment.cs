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
		internal BufferSegment(int size)
		{
			Size = size;
			Buffer = new byte[size];
		}

		public int Size { get; private set; }

		public int Offset { get; private set; }

		public byte[] Buffer { get; private set; }

        public int Write(byte[] data)
		{
			if (data != null)
			{
				var dataLen = data.Length;
				if (dataLen == 0)
					return 0;

				var appendLen = Math.Min(dataLen, Size - Offset);
				if (appendLen > 0)
				{
					Array.Copy(data, Buffer, appendLen);
					Offset += appendLen;
				}

				return dataLen - appendLen;
			}
			return -1;
		}

		public void Reset()
		{
			Offset = 0;
		}
        
		public void Dispose()
		{
			Size = 0;
			Offset = 0;
			Buffer = null;
		}
	}
}
