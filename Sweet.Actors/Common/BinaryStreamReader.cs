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
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Sweet.Actors
{
    public class BinaryStreamReader : Disposable, IStreamReader, IDisposable
    {
        private Stream _input;
        private BinaryReader _reader;

        public BinaryStreamReader(Stream input) 
            : this(input, new UTF8Encoding())
        { }

        public BinaryStreamReader(Stream input, Encoding encoding)
        {
            _input = input;
            _reader = new BinaryReader(input, encoding, true);
        }

        protected override void OnDispose(bool disposing)
        {
            Interlocked.Exchange(ref _input, null);
            using (Interlocked.Exchange(ref _reader, null))
            { }

            base.OnDispose(disposing);
        }

        public long Position
        {
            get
            {
                ThrowIfDisposed();
                return _input.Position;
            }
            set
            {
                ThrowIfDisposed();
                _input.Position = value;
            }
        }

        public bool Closed => Disposed || !((_input?.CanRead ?? false) || (_input?.CanWrite ?? false));

        public int Read(byte[] buffer, int offset, int count)
        {
            ThrowIfDisposed();
            return _reader.Read(buffer, offset, count);
        }

        public Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            return _input.ReadAsync(buffer, offset, count, cancellationToken);
        }

        public bool ReadBoolean()
        {
            ThrowIfDisposed();
            return _reader.ReadBoolean();
        }

        public int ReadByte()
        {
            ThrowIfDisposed();
            return _reader.ReadByte();
        }

        public char ReadChar()
        {
            ThrowIfDisposed();
            return _reader.ReadChar();
        }

        public decimal ReadDecimal()
        {
            ThrowIfDisposed();
            return _reader.ReadDecimal();
        }

        public double ReadDouble()
        {
            ThrowIfDisposed();
            return _reader.ReadDouble();
        }

        public short ReadInt16()
        {
            ThrowIfDisposed();
            return _reader.ReadInt16();
        }

        public int ReadInt32()
        {
            ThrowIfDisposed();
            return _reader.ReadInt32();
        }

        public long ReadInt64()
        {
            ThrowIfDisposed();
            return _reader.ReadInt64();
        }

        public sbyte ReadSByte()
        {
            ThrowIfDisposed();
            return _reader.ReadSByte();
        }

        public float ReadSingle()
        {
            ThrowIfDisposed();
            return _reader.ReadSingle();
        }

        public ushort ReadUInt16()
        {
            ThrowIfDisposed();
            return _reader.ReadUInt16();
        }

        public uint ReadUInt32()
        {
            ThrowIfDisposed();
            return _reader.ReadUInt32();
        }

        public ulong ReadUInt64()
        {
            ThrowIfDisposed();
            return _reader.ReadUInt64();
        }
    }
}
