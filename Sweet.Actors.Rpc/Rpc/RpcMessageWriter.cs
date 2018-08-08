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
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Sweet.Actors.Rpc
{
    public class RpcMessageWriter : Disposable
    {
        private static readonly byte[] ProcessIdBytes = Common.ProcessId.ToBytes();

        private static ArraySliceCache SliceCache = new ArraySliceCache(initialCount: 20, arraySize: RpcMessageSizeOf.EachFrame);

        private IRpcConnection _connnection;

        private int _messageIdSeed;
        private IWireSerializer _serializer;
        private byte[] _serializerKeyBytes = new byte[RpcHeaderSizeOf.SerializerKey];

        public RpcMessageWriter(IRpcConnection conn, string serializerKey)
        {
            _connnection = conn;
            InitializeSerializer(serializerKey);
        }

        protected override void OnDispose(bool disposing)
        {
            Interlocked.Exchange(ref _connnection, null);
        }

        private void InitializeSerializer(string serializerKey)
        {
            serializerKey = serializerKey?.Trim();
            if (String.IsNullOrEmpty(serializerKey))
                serializerKey = Constants.DefaultSerializerKey;

            _serializer = RpcSerializerRegistry.Get(serializerKey);
            if (_serializer == null && serializerKey != Constants.DefaultSerializerKey)
            {
                serializerKey = Constants.DefaultSerializerKey;
                _serializer = RpcSerializerRegistry.Get(serializerKey);
            }

            if (_serializer != null)
            {
                var serializerKeyBytes = Encoding.UTF8.GetBytes(serializerKey);
                Buffer.BlockCopy(serializerKeyBytes, 0, _serializerKeyBytes, 0, Math.Min(_serializerKeyBytes.Length, serializerKeyBytes.Length));
            }
        }

        protected virtual void WriteHeader(Stream outStream, ushort frameCount, out byte[] messageId)
        {
            var headerBuffer = RpcByteBufferCache.HeaderCache.Acquire();
            try
            {
                messageId = BitConverter.GetBytes(Interlocked.Increment(ref _messageIdSeed));

                headerBuffer[RpcHeaderOffsetOf.Sign] = RpcMessageSign.Header;

                Array.Copy(ProcessIdBytes, 0, headerBuffer, RpcHeaderOffsetOf.ProcessId, RpcHeaderSizeOf.ProcessId);
                Array.Copy(messageId, 0, headerBuffer, RpcHeaderOffsetOf.MessageId, RpcHeaderSizeOf.MessageId);
                Array.Copy(_serializerKeyBytes, 0, headerBuffer, RpcHeaderOffsetOf.SerializerKey, RpcHeaderSizeOf.SerializerKey);

                Array.Copy(BitConverter.GetBytes(frameCount), 0, headerBuffer, RpcHeaderOffsetOf.FrameCount, RpcHeaderSizeOf.FrameCount);

                outStream.Write(headerBuffer, 0, RpcMessageSizeOf.Header);
            }
            finally
            {
                RpcByteBufferCache.HeaderCache.Release(headerBuffer);
            }
        }

        private static ushort GetFrameCount(long dataLen)
        {
            return (ushort)(dataLen > 0 ? ((dataLen / RpcMessageSizeOf.EachFrameData) + 1) : 0);
        }

        private bool Write(Stream outStream, WireMessage[] messages, bool flush = true)
        {
            ThrowIfDisposed();

            if (outStream == null)
                return false;

            var dataStream = new ChunkedStream();
            try
            {
                var dataLen = _serializer.Serialize(messages, dataStream);
                if (dataLen > RpcMessageSizeOf.MaxAllowedData)
                    throw new Exception(Errors.MaxAllowedDataSizeExceeded);

                var frameCount = GetFrameCount(dataLen);

                /* Header */
                WriteHeader(outStream, frameCount, out byte[] messageId);

                // Frame count
                if (frameCount == 0)
                    return true;

                using (var slice = SliceCache.Acquire())
                {
                    var buffer = slice.Array;
                    var bufferLen = Math.Min(buffer.Length, RpcMessageSizeOf.EachFrameData);

                    using (var dataReader = dataStream.NewReader())
                    {
                        var headerBuffer = RpcByteBufferCache.HeaderCache.Acquire();
                        try
                        {
                            headerBuffer[RpcFrameHeaderOffsetOf.Sign] = RpcMessageSign.Frame;

                            Array.Copy(ProcessIdBytes, 0, headerBuffer, RpcFrameHeaderOffsetOf.ProcessId, RpcHeaderSizeOf.ProcessId);
                            Array.Copy(messageId, 0, headerBuffer, RpcFrameHeaderOffsetOf.MessageId, RpcHeaderSizeOf.MessageId);

                            var offset = 0;
                            ushort frameIndex = 0;

                            while (frameIndex < frameCount)
                            {
                                Array.Copy(BitConverter.GetBytes(frameIndex++), 0, headerBuffer, RpcFrameHeaderOffsetOf.FrameId, RpcHeaderSizeOf.FrameId);

                                var frameDataLen = dataReader.Read(buffer, 0, Math.Min(bufferLen, (int)(dataLen - offset)));
                                frameDataLen = Math.Max(0, frameDataLen);

                                Array.Copy(BitConverter.GetBytes((ushort)frameDataLen), 0, headerBuffer, RpcFrameHeaderOffsetOf.FrameDataSize, RpcHeaderSizeOf.FrameDataSize);

                                outStream.Write(headerBuffer, 0, RpcMessageSizeOf.FrameHeader);

                                if (frameDataLen > 0)
                                {
                                    outStream.Write(buffer, 0, frameDataLen);
                                    offset += frameDataLen;
                                }
                            }
                        }
                        finally
                        {
                            RpcByteBufferCache.HeaderCache.Release(headerBuffer);
                        }
                    }
                }
            }
            finally
            {
                if (flush)
                    outStream.Flush();
                dataStream.Dispose();
            }
            return true;
        }

        public virtual bool Write(WireMessage[] messages, bool flush = true)
        {
            ThrowIfDisposed();

            var conn = _connnection;
            if (conn != null)
            {
                var socket = conn.Connection;
                // if (socket.IsConnected())
                {
                    var outStream = conn.Out;
                    if (outStream != null)
                        return Write(outStream, messages, flush);

                    using (var bufferStream = new ChunkedStream())
                    {
                        if (Write(bufferStream, messages))
                        {
                            using (var slice = SliceCache.Acquire())
                            {
                                var buffer = slice.Array;

                                using (var reader = bufferStream.NewReader())
                                {
                                    int readLen;
                                    while ((readLen = reader.Read(buffer, 0, buffer.Length)) > 0)
                                    {
                                        socket.Send(buffer, 0, readLen, SocketFlags.None);
                                    }
                                }
                            }

                            return true;
                        }
                    }
                }
            }
            return false;
        }
    }
}
