using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Sweet.Actors
{
    public class ChunkedStream : Stream
    {
        private static readonly byte[] EmptyChunk = new byte[0];
        private static readonly Task<int> ReadCompleted = Task.FromResult(0);

        private bool _isClosed;

        protected int _origin;
        protected long _length;

        protected long _readPosition;
        protected long _writePosition;

        private List<byte[]> _chunks = new List<byte[]>();

        private int _chunkSize = ByteArrayCache.Default.ArraySize;

        private bool _ownsCache;
        private ByteArrayCache _cache = ByteArrayCache.Default;

        private Task<int> _lastReadTask;

        public ChunkedStream()
        { }

        public ChunkedStream(int chunkSize = -1)
        {
            InitializeCache(chunkSize);
        }

        public ChunkedStream(byte[] source, int chunkSize = -1)
        {
            InitializeCache(chunkSize);
            if (source != null)
            {
                var sourceLen = source.Length;
                if (sourceLen > 0)
                {
                    Write(source, 0, sourceLen);

                    ReadPosition = 0L;
                    WritePosition = 0L;
                }
            }
        }

        public ChunkedStream(int length, int chunkSize = -1)
        {
            InitializeCache(chunkSize);
            if (length > 0)
            {
                SetLength(length);
                WritePosition = length;

                ValidateChunks(GetWriteChunkIndex());
                WritePosition = 0L;
            }
        }

        protected override void Dispose(bool disposing)
        {
            _isClosed = true;
            ReleaseChunks(false);

            if (_ownsCache)
            {
                var cache = _cache;
                _cache = null;

                using (cache)
                { }
            }

            base.Dispose(disposing);
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
            get { return WritePosition; }
            set { WritePosition = value; }
        }

        protected int ChunkSize => _chunkSize;

        protected bool IsClosed => _isClosed;

        protected bool OwnsCache => _ownsCache;

        protected ByteArrayCache Cache => _cache;

        public virtual long ReadPosition
        {
            get { return !_isClosed ? _readPosition : 0L; }
            set
            {
                if (!_isClosed)
                    _readPosition = Math.Min(_length, Math.Max(0L, value));
            }
        }

        public virtual long WritePosition
        {
            get { return !_isClosed ? _writePosition : 0L; }
            set
            {
                if (!_isClosed)
                    _writePosition = Math.Min(_length, Math.Max(0L, value));
            }
        }

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

        protected virtual int GetWriteChunkIndex()
        {
            return (int)(WritePosition + _origin) / _chunkSize;
        }

        protected virtual int GetReadChunkIndex()
        {
            return (int)(ReadPosition + _origin) / _chunkSize;
        }

        private byte[] GetWriteChunk()
        {
            if (!_isClosed)
            {
                var writeIndex = GetWriteChunkIndex();
                ValidateChunks(writeIndex);

                return _chunks[writeIndex];
            }
            return EmptyChunk;
        }

        private byte[] GetReadChunk()
        {
            if (!_isClosed)
            {
                var readIndex = GetReadChunkIndex();
                if (readIndex > -1)
                {
                    if (readIndex < _chunks.Count)
                        return _chunks[readIndex];

                    if (readIndex == 0)
                    {
                        var chunk = _cache.Acquire();
                        _chunks.Add(chunk);

                        return chunk;
                    }
                }
            }
            return EmptyChunk;
        }

        protected void ValidateChunks(int index)
        {
            var requiredCnt = (index - _chunks.Count) + 1;

            if (requiredCnt == 1)
                _chunks.Add(_cache.Acquire());
            else if (requiredCnt > 0)
                _chunks.AddRange(_cache.Acquire(requiredCnt));
        }

        protected long GetWriteChunkOffset()
        {
            return (WritePosition + _origin) % _chunkSize;
        }

        protected long GetReadChunkOffset()
        {
            return (ReadPosition + _origin) % _chunkSize;
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
                    ValidateChunks(GetWriteChunkIndex());
                else
                {
                    if (ReadPosition > _length)
                        ReadPosition = _length;

                    if (WritePosition > _length)
                        WritePosition = _length;

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

        public override int Read(byte[] buffer, int offset, int count)
        {
            ValidateReadWrite(buffer, offset, count);

            if (count > 0)
            {
                var remaining = _length - ReadPosition;
                var lCount = Math.Min(remaining, count);

                var readLen = 0;
                do
                {
                    var chunkOffset = (int)GetReadChunkOffset();

                    var copySize = Math.Min(lCount, _chunkSize - chunkOffset);
                    if (copySize < 1)
                        break;

                    Buffer.BlockCopy(GetReadChunk(), chunkOffset, buffer, offset, (int)copySize);

                    lCount -= copySize;
                    offset += (int)copySize;

                    readLen += (int)copySize;
                    ReadPosition += copySize;
                } while (lCount > 0);

                return readLen;
            }
            return 0;
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

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
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
       
        public override long Seek(long offset, SeekOrigin origin)
        {
            ThrowIfDisposed();

            switch (origin)
            {
                case SeekOrigin.Begin:
                    ReadPosition = offset;
                    break;
                case SeekOrigin.Current:
                    ReadPosition += offset;
                    break;
                case SeekOrigin.End:
                    ReadPosition = _length - offset;
                    break;
            }
            return ReadPosition;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            ValidateReadWrite(buffer, offset, count);

            if (count > 0)
            {
                var readPosition = ReadPosition;
                var writePosition = WritePosition;
                try
                {
                    do
                    {
                        var chunkOffset = (int)GetWriteChunkOffset();

                        var copySize = Math.Min(count, _chunkSize - chunkOffset);
                        if (copySize < 1)
                            break;

                        EnsureCapacity(WritePosition + copySize);
                        Buffer.BlockCopy(buffer, offset, GetWriteChunk(), chunkOffset, copySize);

                        count -= copySize;
                        offset += copySize;

                        WritePosition += copySize;
                    } while (count > 0);
                }
                catch (Exception)
                {
                    ReadPosition = readPosition;
                    WritePosition = writePosition;
                    throw;
                }
                finally
                {
                    ReadPosition = readPosition;
                }
            }
        }

        public override Task WriteAsync(Byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            ValidateReadWrite(buffer, offset, count);

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

        protected void EnsureCapacity(long value)
        {
            if (value > _length)
                _length = value;
        }

        public override int ReadByte()
        {
            ThrowIfDisposed();

            if (ReadPosition < _length)
            {
                var chunk = GetReadChunk();
                if (chunk != null && chunk.Length > 0)
                {
                    var result = chunk[GetReadChunkOffset()];
                    ReadPosition++;

                    return result;
                }
            }
            return -1;
        }

        public override void WriteByte(byte value)
        {
            ThrowIfDisposed();

            EnsureCapacity(WritePosition + 1);

            GetWriteChunk()[GetWriteChunkOffset()] = value;
            WritePosition++;
        }

        protected void ReleaseChunks(bool reinit)
        {
            var chunks = _chunks;
            _chunks = reinit ? new List<byte[]>() : null;

            _origin = 0;
            _length = 0L;

            ReadPosition = 0L;
            WritePosition = 0L;

            if (chunks != null && chunks.Count > 0)
            {
                _cache.Release(chunks);
                chunks.Clear();
            }
        }

        public byte[] ToArray()
        {
            if (!_isClosed)
            {
                var readPosition = ReadPosition;
                try
                {
                    ReadPosition = 0L;

                    var result = new byte[_length];
                    Read(result, 0, (int)_length);

                    return result;
                }
                finally
                {
                    ReadPosition = readPosition;
                }
            }
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

        public void WriteTo(Stream destination)
        {
            ThrowIfDisposed();

            var readPosition = ReadPosition;
            try
            {
                ReadPosition = 0;
                CopyTo(destination);
            }
            finally
            {
                ReadPosition = readPosition;
            }
        }

        public void TrimLeft(int trimLength = -1)
        {
            ThrowIfDisposed();

            if (trimLength < 0)
                trimLength = (int)WritePosition;

            if (trimLength > 0)
            {
                if (trimLength >= _length)
                    ReleaseChunks(true);
                else
                {
                    var releaseCnt = (_origin + trimLength) / _chunkSize;

                    while ((releaseCnt-- > 0) && (_chunks.Count > 0))
                    {
                        var chunk = _chunks[0];
                        _chunks.RemoveAt(0);

                        _cache.Release(chunk);
                    }

                    _origin = trimLength % _chunkSize;

                    _length = Math.Max(0, _length - trimLength);

                    ReadPosition = Math.Max(0L, ReadPosition - trimLength);
                    WritePosition = Math.Max(0L, WritePosition - trimLength);

                    ValidateChunks(GetWriteChunkIndex());

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
            }
        }
    }
}
