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
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Sweet.Actors
{
    public class ChunkedStream : Stream
    {
        private static readonly byte[] EmptyChunk = new byte[0];
        private static readonly Task<int> ReadCompleted = Task.FromResult(0);

        private class ChunkedStreamReader : Disposable, IChunkedStreamReader
        {
            private int _chunkSize;
            private long _position;

            private ChunkedStream _stream;

            private List<byte[]> _chunks;
            private Task<int> _lastReadTask;

            private byte[] _buffer = new byte[32];

            public ChunkedStreamReader(ChunkedStream stream, List<byte[]> chunks)
            {
                _chunks = chunks;
                _stream = stream;
                _chunkSize = stream._chunkSize;

                _stream.Changed += StreamChanged;
            }

            protected override void ThrowIfDisposed(string name = null)
            {
                base.ThrowIfDisposed(name);
                if (_stream?._isClosed ?? true)
                    throw new ObjectDisposedException("stream");
            }

            protected override void OnDispose(bool disposing)
            {
                _chunks = null;

                if (_stream != null)
                {
                    _stream.Changed -= StreamChanged;
                    _stream = null;
                }
                base.OnDispose(disposing);
            }

            private void StreamChanged(object sender, ValueChangedEventArgs<long> eventArgs)
            {
                switch (eventArgs.Name)
                {
                    case ValueName.Length:
                        if (eventArgs.NewValue < _position)
                            _position = Math.Max(0, eventArgs.NewValue);
                        break;
                    case ValueName.Origin:
                        _position -= Math.Max(0, _position - (eventArgs.NewValue - eventArgs.OldValue));
                        break;
                }
            }

            public bool Closed => (_stream?._isClosed ?? true) || Disposed;

            public int Origin => (_stream?._origin ?? 0);

            public int ChunkSize => _chunkSize;

            public long Position
            {
                get { return !Closed  ? _position : 0L; }
                set
                {
                    if (!Closed)
                        _position = Math.Min(_stream._length, Math.Max(0L, value));
                }
            }

            private void ValidateReadWrite(byte[] buffer, int offset, int count)
            {
                ThrowIfDisposed();

                if (buffer == null)
                    throw new ArgumentNullException(nameof(buffer));

                if (offset < 0)
                    throw new ArgumentOutOfRangeException(nameof(offset));

                if (count < 0)
                    throw new ArgumentOutOfRangeException(nameof(count));
            }

            public int Read(byte[] buffer, int offset, int count)
            {
                ValidateReadWrite(buffer, offset, count);

                if (count > 0)
                {
                    var stream = _stream;

                    var remaining = stream._length - _position;
                    var lCount = Math.Min(remaining, count);

                    var readLen = 0;
                    do
                    {
                        var chunkOffset = (int)stream.GetChunkOffsetOf(_position);

                        var copySize = Math.Min(lCount, _chunkSize - chunkOffset);
                        if (copySize < 1)
                            break;

                        Buffer.BlockCopy(stream.GetChunkOf(_position), chunkOffset, buffer, offset, (int)copySize);

                        lCount -= copySize;
                        offset += (int)copySize;

                        readLen += (int)copySize;
                        _position += copySize;
                    } while (lCount > 0);

                    return readLen;
                }
                return 0;
            }

            public Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                ValidateReadWrite(buffer, offset, count);

                if (cancellationToken.IsCancellationRequested)
                    return Task.FromCanceled<int>(cancellationToken);

                if (count > 0)
                {
                    try
                    {
                        var readLen = Read(buffer, offset, count);

                        var lastReadTask = _lastReadTask;
                        return (lastReadTask != null && lastReadTask.Result == readLen) ?
                            lastReadTask : (_lastReadTask = Task.FromResult<int>(readLen));
                    }
                    catch (OperationCanceledException)
                    {
                        return Task.FromCanceled<int>(cancellationToken);
                    }
                    catch (Exception e)
                    {
                        return Task.FromException<int>(e);
                    }
                }
                return ReadCompleted;
            }

            public int ReadByte()
            {
                ThrowIfDisposed();

                var stream = _stream;
                if (stream != null && !stream._isClosed && (_position < stream._length))
                {
                    var chunk = stream.GetChunkOf(_position);
                    if (chunk != null && chunk.Length > 0)
                    {
                        var result = chunk[stream.GetChunkOffsetOf(_position)];
                        _position++;

                        return result;
                    }
                }
                return -1;
            }

            public byte[] ToArray()
            {
                var stream = _stream;
                if (stream != null && !stream.Closed)
                {
                    var initialPos = _position;
                    try
                    {
                        _position = 0L;
                        var length = stream._length;

                        var result = new byte[length];
                        Read(result, 0, (int)length);

                        return result;
                    }
                    finally
                    {
                        _position = initialPos;
                    }
                }
                return EmptyChunk;
            }

            private void FillBuffer(int size)
            {
                ThrowIfDisposed();

                if (_stream._length - _position < size)
                    throw new Exception(Errors.EndOfFile);

                var readLen = Read(_buffer, (int)_position, size);
                if (readLen < size)
                    throw new Exception(Errors.EndOfFile);
            }

            public int Read()
            {
                FillBuffer(sizeof(int));
                return _buffer.ToInt(0);
            }

            public bool ReadBoolean()
            {
                FillBuffer(sizeof(bool));
                return _buffer[0] != 0;
            }

            public sbyte ReadSByte()
            {
                FillBuffer(sizeof(byte));
                return (sbyte)_buffer[0];
            }

            public char ReadChar()
            {
                FillBuffer(sizeof(char));
                return _buffer.ToChar(0);
            }

            public short ReadInt16()
            {
                FillBuffer(sizeof(short));
                return _buffer.ToShort(0);
            }

            public ushort ReadUInt16()
            {
                FillBuffer(sizeof(ushort));
                return _buffer.ToUShort(0);
            }

            public int ReadInt32()
            {
                FillBuffer(sizeof(int));
                return _buffer.ToInt(0);
            }

            public uint ReadUInt32()
            {
                FillBuffer(sizeof(uint));
                return _buffer.ToUInt(0);
            }

            public long ReadInt64()
            {
                FillBuffer(sizeof(long));
                return _buffer.ToLong(0);
            }

            public ulong ReadUInt64()
            {
                FillBuffer(sizeof(ulong));
                return _buffer.ToULong(0);
            }

            public float ReadSingle()
            {
                FillBuffer(sizeof(float));
                return _buffer.ToFloat(0);
            }

            public double ReadDouble()
            {
                FillBuffer(sizeof(double));
                return _buffer.ToDouble(0);
            }

            public decimal ReadDecimal()
            {
                FillBuffer(sizeof(decimal));
                return _buffer.ToDecimal(0);
            }
        }

        public class ChunkedStreamWriter : BinaryWriter, IChunkedStreamWriter
        {
            private ChunkedStream _stream;

            private bool _isClosed;
            private BinaryWriter _writer;

            public ChunkedStreamWriter(ChunkedStream stream)
            {
                _stream = stream;
                _writer = new BinaryWriter(stream, new UTF8Encoding(false, true), true);
            }

            private void ThrowIfDisposed(string name = null)
            {
                if (_isClosed)
                    throw new ObjectDisposedException("writer");
                if (_stream?._isClosed ?? true)
                    throw new ObjectDisposedException("stream");
            }

            protected override void Dispose(bool disposing)
            {
                _isClosed = true;
                if (disposing)
                {
                    _stream = null;
                    using (var writer = _writer)
                    {
                        _writer = null;
                    }
                }
                base.Dispose(disposing);
            }

            public int ChunkSize => _stream?.ChunkSize ?? 0;

            public bool Closed => _isClosed || (_stream?.Closed ?? true);

            public int Origin => _stream?.Origin ?? 0;

            public long Position => _stream?.Position ?? 0L;

            public Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                ThrowIfDisposed();
                return _stream.WriteAsync(buffer, offset, count);
            }

        }

        internal enum ValueName
        {
            Length,
            Origin
        }

        internal class ValueChangedEventArgs<T> : EventArgs
        {
            public ValueChangedEventArgs(ValueName name, (T oldValue, T newValue) values)
            {
                Name = name;
                OldValue = values.oldValue;
                NewValue = values.newValue;
            }

            public ValueName Name { get; }

            public T OldValue { get; }

            public T NewValue { get; }
        }

        private bool _isClosed;

        protected int _origin;
        protected long _length;

        protected long _position;

        private bool _release = true;
        private List<byte[]> _chunks = new List<byte[]>();

        private int _chunkSize = ByteArrayCache.Default.ArraySize;

        private bool _ownsCache;
        private ByteArrayCache _cache = ByteArrayCache.Default;

        private ChunkedStreamReader _defaultReader;

        private event EventHandler<ValueChangedEventArgs<long>> Changed;

        public ChunkedStream()
        {
            _defaultReader = new ChunkedStreamReader(this, _chunks);
        }

        public ChunkedStream(int chunkSize = -1)
        {
            InitializeCache(chunkSize);
            _defaultReader = new ChunkedStreamReader(this, _chunks);
        }

        public ChunkedStream(byte[] source, int chunkSize = -1)
        {
            InitializeCache(chunkSize);
            _defaultReader = new ChunkedStreamReader(this, _chunks);

            if (source != null)
            {
                var sourceLen = source.Length;
                if (sourceLen > 0)
                    Write(source, 0, sourceLen);
            }
        }

        public ChunkedStream(IList<ArraySegment<byte>> source, bool release)
        {
            _release = release;
            _defaultReader = new ChunkedStreamReader(this, _chunks);

            if (source != null)
            {
                var cnt = source.Count;
                if (cnt > 0)
                {
                    var chunkSize = -1;

                    foreach (var segment in source)
                    {
                        var chunkLen = segment.Count;
                        if (chunkLen > 0)
                        {
                            if (chunkSize == -1)
                            {
                                chunkSize = Math.Max(ByteArrayCache.Default.ArraySize, chunkLen);                               
                                InitializeCache(chunkSize);
                            }

                            Write(segment.Array, 0, chunkLen);
                        }
                    }
                }
            }
        }

        public ChunkedStream(int length, int chunkSize = -1)
        {
            InitializeCache(chunkSize);
            if (length > 0)
            {
                SetLength(length);
                ValidateIndexedChunk(GetChunkIndexOf(length));
            }
        }

        protected override void Dispose(bool disposing)
        {
            _isClosed = true;
            if (disposing)
            {
                ReleaseChunks(false);

                using (var reader = _defaultReader)
                    _defaultReader = null;

                if (_ownsCache)
                {
                    using (var cache =_cache)
                        _cache = null;
                }
            }
            base.Dispose(disposing);
        }

        public long Capacity
        {
            get
            {
                return _isClosed ? 0L : Math.Max(0L, (_chunkSize * (_chunks?.Count ?? 0)) - _origin);
            }
        }

        public override bool CanRead
        {
            get { return !_isClosed; }
        }

        public override bool CanSeek
        {
            get { return !_isClosed; }
        }

        public override bool CanWrite
        {
            get { return !_isClosed; }
        }

        public override long Length
        {
            get { return !_isClosed ? _length : 0L; }
        }

        public override long Position
        {
            get { return !_isClosed ? _position : 0L; }
            set
            {
                if (!_isClosed)
                    _position = Math.Min(_length, Math.Max(0L, value));
            }
        }

        public long ReadPosition
        {
            get { return !_isClosed ? _defaultReader.Position : 0L; }
            set
            {
                if (!_isClosed)
                    _defaultReader.Position = value;
            }
        }

        public bool Closed => _isClosed;

        protected int ChunkSize => _chunkSize;

        protected bool OwnsCache => _ownsCache;

        protected ByteArrayCache Cache => _cache;

        protected int Origin => _origin;

        protected void InitializeCache(int chunkSize)
        {
            if (chunkSize > 0)
            {
                chunkSize = Math.Min(ByteArrayCache.MaxArraySize,
                    Math.Max(ByteArrayCache.MinArraySize, chunkSize));

                if (chunkSize > ByteArrayCache.DefaultArraySize)
                {
                    _cache = new ByteArrayCache(10, -1, chunkSize);

                    _ownsCache = true;
                    _chunkSize = _cache.ArraySize;
                }
            }
        }

        private int GetChunkIndexOf(long position)
        {
            return position < 0 ? -1 : ((int)(position + _origin) / _chunkSize);
        }

        protected long GetChunkOffsetOf(long position)
        {
            return position < 0 ? -1 : ((position + _origin) % _chunkSize);
        }

        private byte[] GetChunkOf(long position)
        {
            if (!_isClosed)
            {
                var index = GetChunkIndexOf(position);
                if (index > -1)
                {
                    ValidateIndexedChunk(index);
                    if (index < _chunks.Count)
                        return _chunks[index];
                }
            }
            return EmptyChunk;
        }

        protected void ValidateIndexedChunk(int index)
        {
            var requiredCnt = (index - _chunks.Count) + 1;

            if (requiredCnt == 1)
                _chunks.Add(_cache.Acquire());
            else if (requiredCnt > 0)
                _chunks.AddRange(_cache.Acquire(requiredCnt));
        }

        public override void Flush()
        { }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        protected void ThrowIfDisposed()
        {
            if (_isClosed)
                throw new ObjectDisposedException("stream");
        }

        public override void SetLength(long value)
        {
            ThrowIfDisposed();

            value = Math.Max(0, value);
            if (_length != value)
            {
                var initialLen = _length;
                try
                {
                    _length = value;

                    if (_length > initialLen)
                        ValidateIndexedChunk(GetChunkIndexOf(_position));
                    else
                    {
                        if (_position > _length)
                            _position = _length;

                        var requiredCnt = ((_length + _origin) / _chunkSize) + 1;

                        var releaseCnt = _chunks.Count - requiredCnt;
                        if (releaseCnt > 0)
                        {
                            for (var i = _chunks.Count - 1; i < 0; i++)
                            {
                                var index = _chunks.Count - 1;
                                var chunk = _chunks[index];

                                _chunks.RemoveAt(index);
                                if (_release)
                                    _cache.Release(chunk);
                            }
                        }
                    }
                }
                finally
                {
                    OnLengthChanged(initialLen, value);
                }
            }
        }

        private void OnLengthChanged(long oldValue, long newValue)
        {
            Changed?.Invoke(this, new ValueChangedEventArgs<long>(ValueName.Length, (oldValue, newValue)));
        }

        private void OnOriginChanged(long oldValue, long newValue)
        {
            Changed?.Invoke(this, new ValueChangedEventArgs<long>(ValueName.Origin, (oldValue, newValue)));
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ThrowIfDisposed();

            if (count > 0)
                return _defaultReader.Read(buffer, offset, count);
            return 0;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();

            if (count > 0)
                return _defaultReader.ReadAsync(buffer, offset, count, cancellationToken);
            return ReadCompleted;
        }
       
        public override long Seek(long offset, SeekOrigin origin)
        {
            ThrowIfDisposed();

            switch (origin)
            {
                case SeekOrigin.Begin:
                    _position = offset;
                    break;
                case SeekOrigin.Current:
                    _position += offset;
                    break;
                case SeekOrigin.End:
                    _position = _length - offset;
                    break;
            }
            return _position;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            ThrowIfDisposed();

            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));

            if (offset < 0)
                throw new ArgumentOutOfRangeException(nameof(offset));

            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count));

            if (count > 0)
            {
                var initialLen = _length;
                var initialPos = _position;
                try
                {
                    do
                    {
                        var chunkOffset = (int)GetChunkOffsetOf(_position);

                        var copySize = Math.Min(count, _chunkSize - chunkOffset);
                        if (copySize < 1)
                            break;

                        EnsureCapacity(_position + copySize);
                        Buffer.BlockCopy(buffer, offset, GetChunkOf(_position), chunkOffset, copySize);

                        count -= copySize;
                        offset += copySize;

                        _position += copySize;
                    } while (count > 0);
                }
                catch (Exception)
                {
                    _position = initialPos;
                    throw;
                }
                finally
                {
                    if (initialLen != _length)
                        OnLengthChanged(initialLen, _length);
                }
            }
        }

        public override Task WriteAsync(Byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();

            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));

            if (offset < 0)
                throw new ArgumentOutOfRangeException(nameof(offset));

            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count));

            if (buffer.Length - offset < count)
                throw new ArgumentOutOfRangeException(nameof(count));

            if (cancellationToken.IsCancellationRequested)
                return Task.FromCanceled(cancellationToken);

            try
            {
                Write(buffer, offset, count);
                return Task.CompletedTask;
            }
            catch (OperationCanceledException)
            {
                return Task.FromCanceled(cancellationToken);
            }
            catch (Exception e)
            {
                return Task.FromException(e);
            }
        }

        public override int ReadByte()
        {
            ThrowIfDisposed();

            if (_defaultReader != null)
                return _defaultReader.ReadByte();
            return -1;
        }

        public override void WriteByte(byte value)
        {
            ThrowIfDisposed();

            var position = _position;
            var initialLen = _length;
            try
            {
                EnsureCapacity(position + 1);

                GetChunkOf(position)[GetChunkOffsetOf(position)] = value;
                _position++;
            }
            finally
            {
                if (initialLen != _length)
                    OnLengthChanged(initialLen, _length);
            }
        }

        protected void EnsureCapacity(long value)
        {
            if (value > _length)
                _length = value;
        }

        protected void ReleaseChunks(bool reinit)
        {
            var chunks = _chunks;
            _chunks = reinit ? new List<byte[]>() : null;

            var initialLen = _length;
            var initialOrigin = _origin;
            try
            {
                _origin = 0;
                _length = 0L;

                _position = 0L;

                if (chunks != null && chunks.Count > 0)
                {
                    if (_release)
                        _cache.Release(chunks);
                    chunks.Clear();
                }
            }
            finally
            {
                if (initialOrigin != _origin)
                    OnLengthChanged(initialLen, _origin);

                if (initialLen != _length)
                    OnLengthChanged(initialLen, _length);
            }
        }

        public IChunkedStreamReader NewReader()
        {
            ThrowIfDisposed();
            return new ChunkedStreamReader(this, _chunks);
        }

        public IChunkedStreamWriter NewWriter()
        {
            ThrowIfDisposed();
            return new ChunkedStreamWriter(this);
        }

        public byte[] ToArray()
        {
            ThrowIfDisposed();

            if (_defaultReader != null)
                return _defaultReader.ToArray();
            return EmptyChunk;
        }

        public void ReadFrom(Stream source, long length)
        {
            ThrowIfDisposed();

            if (source == null)
                throw new ArgumentNullException(nameof(source));

            if (length > 0)
            {
                const int bufferSize = 4096;

                var buffer = new byte[bufferSize];
                do
                {
                    var readLen = source.Read(buffer, 0, (int)Math.Min(bufferSize, length));

                    length -= readLen;
                    Write(buffer, 0, readLen);
                } while (length > 0);
            }
        }

        public void TrimLeft(int trimLength = -1)
        {
            ThrowIfDisposed();

            if (trimLength < 0)
                trimLength = (int)_position;

            if (trimLength > 0)
            {
                if (trimLength >= _length)
                    ReleaseChunks(true);
                else
                {
                    var initialLen = _length;
                    var initialOrigin = _origin;
                    try
                    {
                        var releaseCnt = (_origin + trimLength) / _chunkSize;

                        while ((releaseCnt-- > 0) && (_chunks.Count > 0))
                        {
                            var chunk = _chunks[0];
                            _chunks.RemoveAt(0);

                            if (_release)
                                _cache.Release(chunk);
                        }

                        _origin = trimLength % _chunkSize;

                        _length = Math.Max(0, _length - trimLength);

                        var position = Math.Max(0L, _position - trimLength);
                        _position = position;

                        ValidateIndexedChunk(GetChunkIndexOf(position));

                        if (_origin > 0 && _length > 0 &&
                            _chunks.Count == 1)
                        {
                            var copySize = (int)(_length - _origin);
                            if (copySize > 0)
                            {
                                var chunk = _chunks[0];
                                Buffer.BlockCopy(chunk, _origin, chunk, 0, copySize);
                            }

                            _origin = 0;
                        }
                    }
                    finally
                    {
                        if (initialOrigin != _origin)
                            OnOriginChanged(initialOrigin, _origin);

                        if (initialLen != _length)
                            OnLengthChanged(initialLen, _length);
                    }
                }
            }
        }
    }
}
