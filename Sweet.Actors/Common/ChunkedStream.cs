using System;
using System.Collections.Generic;
using System.IO;

namespace Sweet.Actors
{
    public class ChunkedStream : Stream
    {
        private static readonly byte[] EmptyChunk = new byte[0];

        private bool _isClosed;

        private int _origin = 0;

        private long _length;
        private long _position;

        private int _chunkSize = ByteArrayCache.Default.ArraySize;
        private List<byte[]> _chunks = new List<byte[]>();

        private bool _ownsCache;
        private ByteArrayCache _cache = ByteArrayCache.Default;

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
                    _position = 0;
                }
            }
        }

        public ChunkedStream(int length, int chunkSize = -1)
        {
            InitializeCache(chunkSize);
            if (length > 0)
            {
                SetLength(length);
                _position = length;

                ValidateChunks();
                _position = 0;
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
            get { return !_isClosed ? _position : 0L; }
            set { if (!_isClosed) _position = Math.Max(0L, value); }
        }

        private void InitializeCache(int chunkSize)
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

        private byte[] GetCurrentChunk()
        {
            if (!_isClosed)
            {
                ValidateChunks();

                var index = (int)(_position + _origin) / _chunkSize;
                return _chunks[index];
            }
            return EmptyChunk;
        }

        private void ValidateChunks()
        {
            var index = (int)(_position + _origin) / _chunkSize;

            var requiredCnt = (index - _chunks.Count) + 1;
            if (requiredCnt == 1)
                _chunks.Add(_cache.Acquire());
            else if (requiredCnt > 0)
                _chunks.AddRange(_cache.Acquire(requiredCnt));
        }

        private long GetChunkOffset()
        {
            return (_position + _origin) % _chunkSize;
        }

        public override void Flush()
        { }

        private void ThrowIfDisposed()
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
                    ValidateChunks();
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

        public override int Read(byte[] buffer, int offset, int count)
        {
            ThrowIfDisposed();

            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count));

            if (count > 0)
            {
                if (buffer == null)
                    throw new ArgumentNullException(nameof(buffer));

                if (offset < 0)
                    throw new ArgumentOutOfRangeException(nameof(offset));

                var remaining = _length - _position;
                var lCount = Math.Min(remaining, count);

                var readLen = 0;
                do
                {
                    var chunkOffset = (int)GetChunkOffset();

                    var copySize = Math.Min(lCount, _chunkSize - chunkOffset);
                    if (copySize < 1)
                        break;

                    Buffer.BlockCopy(GetCurrentChunk(), chunkOffset, buffer, offset, (int)copySize);

                    lCount -= copySize;
                    offset += (int)copySize;

                    readLen += (int)copySize;
                    _position += copySize;
                } while (lCount > 0);

                return readLen;
            }
            return 0;
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

            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count));

            if (count > 0)
            {
                if (buffer == null)
                    throw new ArgumentNullException(nameof(buffer));

                if (offset < 0)
                    throw new ArgumentOutOfRangeException(nameof(offset));

                var initialPosition = _position;
                try
                {
                    do
                    {
                        var chunkOffset = (int)GetChunkOffset();

                        var copySize = Math.Min(count, _chunkSize - chunkOffset);
                        if (copySize < 1)
                            break;

                        EnsureCapacity(_position + copySize);
                        Buffer.BlockCopy(buffer, offset, GetCurrentChunk(), chunkOffset, copySize);

                        count -= copySize;
                        offset += copySize;

                        _position += copySize;
                    } while (count > 0);
                }
                catch (Exception)
                {
                    _position = initialPosition;
                    throw;
                }
            }
        }

        private void EnsureCapacity(long capacity)
        {
            if (capacity > _length)
                _length = capacity;
        }

        public override int ReadByte()
        {
            ThrowIfDisposed();

            if (_position < _length)
            {
                var chunk = GetCurrentChunk();
                if (chunk != null)
                {
                    var result = chunk[GetChunkOffset()];
                    _position++;

                    return result;
                }
            }
            return -1;
        }

        public override void WriteByte(byte value)
        {
            ThrowIfDisposed();

            EnsureCapacity(_position + 1);

            GetCurrentChunk()[GetChunkOffset()] = value;
            _position++;
        }

        private void ReleaseChunks(bool reinit)
        {
            var chunks = _chunks;
            _chunks = reinit ? new List<byte[]>() : null;

            _origin = 0;
            _length = 0L;
            _position = 0L;

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
                var initialPosition = _position;
                _position = 0;

                var result = new byte[_length];
                Read(result, 0, (int)_length);

                _position = initialPosition;
                return result;
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

            var initialPosition = _position;
            _position = 0;

            CopyTo(destination);
            _position = initialPosition;
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
                    var releaseCnt = (_origin + trimLength) / _chunkSize;

                    while ((releaseCnt-- > 0) && (_chunks.Count > 0))
                    {
                        var chunk = _chunks[0];
                        _chunks.RemoveAt(0);

                        _cache.Release(chunk);
                    }

                    _origin = trimLength % _chunkSize;

                    _length = Math.Max(0, _length - trimLength);
                    _position = Math.Max(0, _position - trimLength);

                    ValidateChunks();

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
