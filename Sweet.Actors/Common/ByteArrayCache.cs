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
    public sealed class ByteArrayCache : ObjectCacheBase<byte[]>
    {
        public static readonly ByteArrayCache Default = new ByteArrayCache(10);

        public const int MinArraySize = 512;
        public const int MaxArraySize = 4 * Constants.MB;
        public const int DefaultArraySize = 8 * Constants.KB;

        private int _arraySize;

        public ByteArrayCache(int initialCount = 0, int limit = DefaultLimit, int arraySize = DefaultArraySize)
            : base(ArrayProvider, 0, limit)
        {
            if (arraySize < 1)
                _arraySize = DefaultArraySize;
            else
                _arraySize = Math.Min(MaxArraySize, Math.Max(MinArraySize, arraySize));

            initialCount = Math.Max(0, initialCount);
            if (initialCount > 0)
                for (var i = 0; i < initialCount; i++)
                    Enqueue(ArrayProvider(this));
        }

        public int ArraySize => _arraySize;

        private static byte[] ArrayProvider(ObjectCacheBase<byte[]> cache)
        {
            return new byte[(((ByteArrayCache)cache)._arraySize)];
        }

        protected override void OnDispose(byte[] item)
        { }

        protected override void OnEnqueue(byte[] item)
        { }
    }
}
