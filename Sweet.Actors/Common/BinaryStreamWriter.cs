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
    public class BinaryStreamWriter : Disposable, IStreamWriter, IDisposable
    {
        private Stream _input;
        private BinaryWriter _writer;

        public BinaryStreamWriter(Stream input)
            : this(input, Encoding.UTF8)
        { }

        public BinaryStreamWriter(Stream input, Encoding encoding)
        {
            _input = input;
            _writer = new BinaryWriter(input, encoding, true);
        }

        protected override void OnDispose(bool disposing)
        {
            Interlocked.Exchange(ref _input, null);
            using (Interlocked.Exchange(ref _writer, null))
            { }
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

        public Stream BaseStream => _input;

        public bool Closed => Disposed || !((_input?.CanRead ?? false) || (_input?.CanWrite ?? false));

        public virtual void Write(byte[] buffer)
        {
            ThrowIfDisposed();
            _writer.Write(buffer);
        }

        public virtual void Write(byte[] buffer, int offset, int count)
        {
            ThrowIfDisposed();
            _writer.Write(buffer, offset, count);
        }

        public virtual Task WriteAsync(byte[] buffer, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            throw new NotImplementedException();
        }

        public virtual Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            throw new NotImplementedException();
        }

        public virtual void Write(byte value)
        {
            ThrowIfDisposed();
            _writer.Write(value);
        }

        public virtual void Write(bool value)
        {
            ThrowIfDisposed();
            _writer.Write(value);
        }

        public virtual void Write(sbyte value)
        {
            ThrowIfDisposed();
            _writer.Write(value);
        }

        public virtual void Write(char value)
        {
            ThrowIfDisposed();
            _writer.Write(value);
        }

        public virtual void Write(short value)
        {
            ThrowIfDisposed();
            _writer.Write(value);
        }

        public virtual void Write(ushort value)
        {
            ThrowIfDisposed();
            _writer.Write(value);
        }

        public virtual void Write(int value)
        {
            ThrowIfDisposed();
            _writer.Write(value);
        }

        public virtual void Write(uint value)
        {
            ThrowIfDisposed();
            _writer.Write(value);
        }

        public virtual void Write(long value)
        {
            ThrowIfDisposed();
            _writer.Write(value);
        }

        public virtual void Write(ulong value)
        {
            ThrowIfDisposed();
            _writer.Write(value);
        }

        public virtual void Write(float value)
        {
            ThrowIfDisposed();
            _writer.Write(value);
        }

        public virtual void Write(double value)
        {
            ThrowIfDisposed();
            _writer.Write(value);
        }

        public virtual void Write(decimal value)
        {
            ThrowIfDisposed();
            _writer.Write(value);
        }
    }
}
