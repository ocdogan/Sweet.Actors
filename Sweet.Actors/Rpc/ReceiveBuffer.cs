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
using System.Threading;

using Microsoft.IO;

namespace Sweet.Actors
{
    internal class ReceiveBuffer : Disposable
    {
        private const int BlockSize = 16 * Constants.KB;
        private const int LargeBufferMultiple = 1 << 20;
        private const int MaximumBufferSize = 8 * (1 << 20);

        private static readonly RecyclableMemoryStreamManager StreamManager = 
            new RecyclableMemoryStreamManager(BlockSize, LargeBufferMultiple, MaximumBufferSize);

        private RecyclableMemoryStream _stream;

        public ReceiveBuffer()
        {
            _stream = new RecyclableMemoryStream(StreamManager);
        }

        protected override void OnDispose(bool disposing)
        {
            using (Interlocked.Exchange(ref _stream, null))
            { }
        }

        public void OnReceiveData(byte[] buffer, int bytesTransferred)
		{
            if ((buffer != null) && (bytesTransferred > 0))
                _stream.Write(buffer, 0, bytesTransferred);
        }

		internal bool TryDecodeMessage(out Message msg)
		{
			msg = null;
            var stream = _stream;

            var segmentLen = stream?.Length ?? 0;
            if (segmentLen >= Constants.HeaderSize)
            {
                if (stream.ReadByte() != Constants.HeaderSign)
                    throw new Exception(Errors.InvalidMessageType);

                var receivedMsg = new ReceivedMessage();

                stream.Position = 0;
            }
			return false;
        }
    }
}
