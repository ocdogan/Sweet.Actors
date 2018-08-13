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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Sweet.Actors
{
    public class ChunkedStream : Stream
    {
        private static int IdSeed;

        private static readonly byte[] EmptyChunk = new byte[0];
        private static readonly Task<int> ReadCompleted = Task.FromResult(0);

        private static readonly int DefaultCacheSize = ByteArrayCache.Default.ArraySize;

        private static readonly ConcurrentDictionary<int, ByteArrayCache> GlobalByteArrayCache =
            new ConcurrentDictionary<int, ByteArrayCache>();

        private class ChunkedStreamReader : Disposable, IChunkedStreamReader
        {
            private int _chunkSize;

            private IStreamReader _reader;
            private ChunkedStream _input;

            private int _initialOrigin;
            private bool _initialIsEOF;
            private long _initialPosition = -1;

            private static int _readerCount;

            private byte[] _buffer = new byte[16];

            public ChunkedStreamReader(ChunkedStream input, long position = -1)
            {
                _input = input;
                _chunkSize = input._chunkSize;

                if (position > -1)
                {
                    _initialOrigin = input._origin;
                    _initialPosition = input._position;
                    _initialIsEOF = _initialPosition == input._length;

                    input.Position = position;
                }

                Interlocked.Increment(ref _readerCount);
            }

            protected override void ThrowIfDisposed(string name = null)
            {
                base.ThrowIfDisposed(name);
                if (_input?._isClosed ?? true)
                    throw new ObjectDisposedException("stream");
            }

            protected override void OnDispose(bool disposing)
            {
                Interlocked.Add(ref _readerCount, -1);

                var input = Interlocked.Exchange(ref _input, null);
                if (input != null && _initialPosition > -1)
                    input.Position = _initialIsEOF ? input.Length : _initialPosition;
            }

            public bool Closed => (_input?._isClosed ?? true) || Disposed;

            public int Origin => (_input?._origin ?? 0);

            public int ChunkSize => _chunkSize;

            public long Position
            {
                get { return (_input?._position ?? 0); }
                set
                {
                    if (!Closed && _input != null)
                        _input.Position = value;
                }
            }

            public Stream BaseStream => _input;

            private void FillBuffer(int size)
            {
                ThrowIfDisposed();

                var readLen = _input?.Read(_buffer, 0, size);
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

            public string ReadString()
            {
                ThrowIfDisposed();

                var reader = _reader;
                if (reader == null)
                    reader = (_reader = new BinaryStreamReader(_input));

                return reader.ReadString();
            }

            public byte[] ReadBytes(int count)
            {
                if (count < 0)
                    throw new ArgumentOutOfRangeException(nameof(count));

                var result = new byte[count];
                if (count > 0)
                {
                    var readLen = Read(result, 0, count);
                    if (readLen != count)
                        throw new ArgumentOutOfRangeException(nameof(count));
                }
                return result;
            }

            public byte[] ToArray()
            {
                ThrowIfDisposed();
                return _input?.ToArray() ?? EmptyChunk;
            }

            public int Read(byte[] buffer, int offset, int count)
            {
                ThrowIfDisposed();
                return _input?.Read(buffer, offset, count) ?? 0;
            }

            public Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                ThrowIfDisposed();
                return _input?.ReadAsync(buffer, offset, count) ??
                    Task.FromException<int>(new Exception(Errors.EndOfFile)); 
            }

            public int ReadByte()
            {
                ThrowIfDisposed();
                return _input?.ReadByte() ?? -1;
            }
        }

        public class ChunkedStreamWriter : BinaryStreamWriter, IChunkedStreamWriter
        {
            private bool _isClosed;
            private ChunkedStream _input;

            public ChunkedStreamWriter(ChunkedStream input)
                : base(input, Encoding.UTF8)
            {
                _input = input;
            }

            protected override void ThrowIfDisposed(string name = null)
            {
                if (_isClosed)
                    throw new ObjectDisposedException("writer");
                if (_input?._isClosed ?? true)
                    throw new ObjectDisposedException("stream");
            }

            protected override void OnDispose(bool disposing)
            {
                _isClosed = true;
                if (disposing)
                    _input = null;
                base.OnDispose(disposing);
            }

            public int ChunkSize => _input?.ChunkSize ?? 0;

            public int Origin => _input?.Origin ?? 0;

            public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                ThrowIfDisposed();
                return _input.WriteAsync(buffer, offset, count);
            }

            public override Task WriteAsync(byte[] buffer, CancellationToken cancellationToken)
            {
                ThrowIfDisposed();
                return _input.WriteAsync(buffer, 0, buffer?.Length ?? 0);
            }
        }

        private int _id;

        private bool _isClosed;

        protected int _origin;
        protected long _length;

        protected long _position;

        protected int _readTimeout = Timeout.Infinite;
        protected int _writeTimeout = Timeout.Infinite;

        private List<byte[]> _chunks = new List<byte[]>();

        private int _chunkSize = ByteArrayCache.Default.ArraySize;

        private bool _ownsCache;
        private ByteArrayCache _cache = ByteArrayCache.Default;

        private Task<int> _lastReadTask;

        private ChunkedStreamReader _defaultReader;
        private object _defaultReaderLock = new object();

        public ChunkedStream()
            : this(DefaultCacheSize)
        { }

        public ChunkedStream(int chunkSize = -1)
        {
            _id = Interlocked.Increment(ref IdSeed);
            InitializeCache(chunkSize);
        }

        public ChunkedStream(byte[] source, int chunkSize = -1)
            : this(chunkSize)
        {
            if (source != null)
            {
                var sourceLen = source.Length;
                if (sourceLen > 0)
                    Write(source, 0, sourceLen);
            }
        }

        public ChunkedStream(IList<byte[]> source)
            : this(DefaultCacheSize)
        {
            var sourceCount = source?.Count ?? 0;
            if (sourceCount > 0)
            {
                foreach (var segment in source)
                {
                    var chunkLen = segment?.Length ?? 0;
                    if (chunkLen > 0)
                        Write(segment, 0, chunkLen);
                }
            }
        }

        public ChunkedStream(IList<ArraySegment<byte>> source)
            : this(DefaultCacheSize)
        {
            var sourceCount = source?.Count ?? 0;
            if (sourceCount > 0)
            {
                foreach (var segment in source)
                {
                    var chunkLen = segment.Count;
                    if (chunkLen > 0)
                        Write(segment.Array, segment.Offset, chunkLen);
                }
            }
        }

        private ChunkedStreamReader GetDefaultReader()
        {
            if (!_isClosed)
            {
                var result = _defaultReader;
                if (result == null)
                {
                    lock (_defaultReaderLock)
                    {
                        result = _defaultReader;
                        if (result == null)
                            result = (_defaultReader = new ChunkedStreamReader(this));
                    }
                }
                return result;
            }
            return null;
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

                using (Interlocked.Exchange(ref _defaultReader, null)) { }

                if (_ownsCache)
                    using (Interlocked.Exchange(ref _cache, null)) { }
            }
            base.Dispose(disposing);
        }

        public long Capacity => _isClosed ? 0L :
                    Math.Max(0L, (_chunkSize * (_chunks?.Count ?? 0)) - _origin);

        public override bool CanRead => !_isClosed;

        public override bool CanSeek => !_isClosed;

        public override bool CanWrite => !_isClosed;

        public override long Length => !_isClosed ? _length : 0L;

        public override long Position
        {
            get { return !_isClosed ? _position : 0L; }
            set
            {
                if (!_isClosed)
                    _position = Math.Min(_length, Math.Max(0L, value));
            }
        }

        public bool Closed => _isClosed;

        public int ChunkSize => _chunkSize;

        protected bool OwnsCache => _ownsCache;

        protected ByteArrayCache Cache => _cache;

        public int Origin => _origin;

        public override int ReadTimeout { get => _readTimeout; set => _readTimeout = value; }

        public override int WriteTimeout { get => _writeTimeout; set => _writeTimeout = value; }

        protected void InitializeCache(int chunkSize)
        {
            if (chunkSize > 0)
            {
                var chunkStep = 4 * Constants.KB;
                if (chunkSize % chunkStep > 0)
                    chunkSize += chunkStep - chunkSize;

                chunkSize = Math.Min(ByteArrayCache.MaxArraySize, Math.Max(DefaultCacheSize, chunkSize));

                if (chunkSize != DefaultCacheSize)
                {
                    _cache =
                        GlobalByteArrayCache.GetOrAdd(chunkSize, (size) => new ByteArrayCache(10, -1, size));

                    _ownsCache = true;
                    _chunkSize = _cache.ArraySize;
                }
            }
        }

        private int GetChunkIndexOf(long position)
        {
            return position > -1 ? ((int)(position + _origin) / _chunkSize) : -1;
        }

        protected long GetChunkOffsetOf(long position)
        {
            return position > -1 ? ((position + _origin) % _chunkSize) : -1L;
        }

        private byte[] GetChunkOf(long position)
        {
            if (!_isClosed)
            {
                var index = GetChunkIndexOf(position);

                ValidateIndexedChunk(index);
                return _chunks[index];
            }
            return EmptyChunk;
        }

        protected void ValidateIndexedChunk(int index)
        {
            var requiredCnt = (index - _chunks.Count) + 1;
            if (requiredCnt > 0)
            {
                if (requiredCnt == 1)
                    _chunks.Add(_cache.Acquire());
                else
                    _chunks.AddRange(_cache.Acquire(requiredCnt));
            }
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

                            _cache.Release(chunk);
                        }
                    }
                }
            }
        }

        private void ValidateReadWrite(byte[] buffer, int offset, int count)
        {
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));

            if (offset < 0)
                throw new ArgumentOutOfRangeException(nameof(offset));

            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count));
        }

        private int ReadFrom(long position, byte[] buffer, int bufferOffset, int count)
        {
            if (count < 1)
                return 0;

            var remaining = Math.Min(_length - position, count);
            if (remaining <= 0)
                return 0;

            var readLen = 0;
            do
            {
                var chunkOffset = (int)GetChunkOffsetOf(position);

                var copySize = Math.Min(remaining, _chunkSize - chunkOffset);
                if (copySize < 1)
                    break;

                Buffer.BlockCopy(GetChunkOf(position), chunkOffset, buffer, bufferOffset, (int)copySize);

                remaining -= copySize;
                bufferOffset += (int)copySize;

                readLen += (int)copySize;
                position += copySize;
            } while (remaining > 0);

            return readLen;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ThrowIfDisposed();
            ValidateReadWrite(buffer, offset, count);

            var readLen = ReadFrom(_position, buffer, offset, count);
            if (readLen > 0)
                _position += readLen;
            return readLen;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            ValidateReadWrite(buffer, offset, count);

            if (cancellationToken.IsCancellationRequested)
                return Task.FromCanceled<int>(cancellationToken);

            if (count > 0)
            {
                try
                {
                    var readLen = ReadFrom(_position, buffer, offset, count);

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

        public override int ReadByte()
        {
            ThrowIfDisposed();

            if (!Closed)
            {
                var position = _position;
                if (position < _length)
                {
                    var chunk = GetChunkOf(position);
                    if (chunk != null && chunk.Length > 0)
                    {
                        var result = chunk[GetChunkOffsetOf(position)];
                        _position++;

                        return result;
                    }
                }
            }
            return -1;
        }

        public int ReadFrom(Stream source, long length)
        {
            ThrowIfDisposed();

            if (source == null)
                throw new ArgumentNullException(nameof(source));

            if (length < 0)
                throw new ArgumentOutOfRangeException(nameof(length));

            var result = 0;
            if (length > 0)
            {
                var buffer = _cache.Acquire();
                try
                {
                    var bufferLen = buffer.Length;
                    do
                    {
                        var readLen = source.Read(buffer, 0, (int)Math.Min(bufferLen, length));
                        if (readLen <= 0)
                            break;

                        length -= readLen;

                        Write(buffer, 0, readLen);
                        result += readLen;
                    } while (length > 0);
                }
                finally
                {
                    _cache.Release(buffer);
                }
            }
            return result;
        }

        public byte[] ToArray()
        {
            ThrowIfDisposed();

            if (!Closed)
            {
                var initialPos = _position;
                try
                {
                    _position = 0L;
                    var length = _length;

                    var result = new byte[length];
                    ReadFrom(0, result, 0, (int)length);

                    return result;
                }
                finally
                {
                    _position = initialPos;
                }
            }
            return EmptyChunk;
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
            ValidateReadWrite(buffer, offset, count);

            if (buffer.Length - offset < count)
                throw new ArgumentOutOfRangeException(nameof(count));

            WriteInternal(buffer, offset, count);
        }

        private void WriteInternal(byte[] buffer, int offset, int count)
        {
            if (count > 0)
            {
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
            }
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            ValidateReadWrite(buffer, offset, count);

            if (buffer.Length - offset < count)
                throw new ArgumentOutOfRangeException(nameof(count));

            if (cancellationToken.IsCancellationRequested)
                return Task.FromCanceled(cancellationToken);

            try
            {
                WriteInternal(buffer, offset, count);
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

        public override void WriteByte(byte value)
        {
            ThrowIfDisposed();

            var position = _position;
            EnsureCapacity(position + 1);

            GetChunkOf(position)[GetChunkOffsetOf(position)] = value;
            _position++;
        }

        protected void EnsureCapacity(long value)
        {
            if (value > _length)
                _length = value;
        }

        protected void ReleaseChunks(bool reinit)
        {
            var chunks = Interlocked.Exchange(ref _chunks, (reinit && !_isClosed) ? new List<byte[]>() : null);

            _origin = 0;
            _length = 0L;
            _position = 0L;

            if ((chunks?.Count ?? 0) > 0)
            {
                _cache.Release(chunks);
                chunks.Clear();
            }
        }

        public IChunkedStreamReader NewReader(long position = -1)
        {
            ThrowIfDisposed();
            return new ChunkedStreamReader(this, position);
        }

        public IChunkedStreamWriter NewWriter()
        {
            ThrowIfDisposed();
            return new ChunkedStreamWriter(this);
        }

        public void TrimLeft(int trimLength = -1)
        {
            ThrowIfDisposed();

            if (_chunks == null)
                return;

            if (trimLength < 0)
                trimLength = (int)_position;

            if (trimLength <= 0)
                return;

            var newOrigin = _origin + trimLength;
            if (newOrigin > _origin + _length)
                throw new ArgumentOutOfRangeException(nameof(trimLength));

            var chunkCount = _chunks.Count;

            var releaseCnt = Math.Min(chunkCount, (int)(newOrigin / _chunkSize));
            if (releaseCnt > 0 && releaseCnt >= chunkCount)
            {
                ReleaseChunks(true);
                return;
            }

            while ((releaseCnt-- > 0) && (_chunks.Count > 0))
            {
                var chunk = _chunks[0];
                _chunks.RemoveAt(0);

                newOrigin -= _chunkSize;

                _cache.Release(chunk);
            }

            _origin = newOrigin;
            _length = Math.Max(0L, _length - trimLength);
            _position = Math.Max(0L, _position - trimLength);

            ValidateIndexedChunk(GetChunkIndexOf(_position));
        }
    }
}
