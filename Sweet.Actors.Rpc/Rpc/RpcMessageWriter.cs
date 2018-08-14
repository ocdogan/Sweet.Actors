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

        private const int SendRetryTreshold = 100;

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

        protected virtual void WriteHeader(Stream outStream, int dataSize, out byte[] messageId)
        {
            var headerBuffer = RpcByteBufferCache.HeaderCache.Acquire();
            try
            {
                messageId = BitConverter.GetBytes(Interlocked.Increment(ref _messageIdSeed));

                headerBuffer[RpcHeaderOffsetOf.Sign] = RpcMessageSign.Header;

                Array.Copy(ProcessIdBytes, 0, headerBuffer, RpcHeaderOffsetOf.ProcessId, RpcHeaderSizeOf.ProcessId);
                Array.Copy(messageId, 0, headerBuffer, RpcHeaderOffsetOf.MessageId, RpcHeaderSizeOf.MessageId);
                Array.Copy(_serializerKeyBytes, 0, headerBuffer, RpcHeaderOffsetOf.SerializerKey, RpcHeaderSizeOf.SerializerKey);

                Array.Copy(BitConverter.GetBytes(dataSize), 0, headerBuffer, RpcHeaderOffsetOf.DataSize, RpcHeaderSizeOf.DataSize);

                outStream.Write(headerBuffer, 0, RpcMessageSizeOf.Header);
            }
            finally
            {
                RpcByteBufferCache.HeaderCache.Release(headerBuffer);
            }
        }

        private bool Write(ChunkedStream outStream, WireMessage[] messages, bool flush = true)
        {
            ThrowIfDisposed();

            if (outStream == null)
                return false;

            using (var dataStream = new ChunkedStream())
            {
                try
                {
                    var dataSize = (int)_serializer.Serialize(messages, dataStream);
                    if (dataSize > RpcMessageSizeOf.MaxAllowedData)
                        return false;

                    /* Header */
                    WriteHeader(outStream, dataSize, out byte[] messageId);

                    if (dataSize == 0)
                        return true;

                    dataStream.Position = 0;
                    outStream.ReadFrom(dataStream, dataSize);

                    /* var buffer = ByteArrayCache.Default.Acquire();
                    try
                    {
                        var bufferLen = buffer.Length;

                        using (var dataReader = dataStream.NewReader(0))
                        {
                            var readLen = 0;
                            while (dataSize > 0)
                            {
                                readLen = dataReader.Read(buffer, 0, bufferLen);

                                readLen = Math.Max(0, readLen);
                                if (readLen > 0)
                                {
                                    dataSize -= readLen;
                                    bufferLen = Math.Min(bufferLen, dataSize);

                                    outStream.Write(buffer, 0, readLen);
                                }
                            }
                        }
                    }
                    finally
                    {
                        ByteArrayCache.Default.Release(buffer);
                    } */
                }
                finally
                {
                    if (flush)
                        outStream.Flush();
                }
            }
            return true;
        }

        public virtual bool Write(WireMessage[] messages, bool flush = true)
        {
            ThrowIfDisposed();

            var conn = _connnection;
            if (conn == null)
                return false;

            var socket = conn.Connection;
            if (!socket.IsConnected())
                return false;

            using (var stream = new ChunkedStream())
            {
                if (!Write(stream, messages))
                    return false;

                var buffer = ByteArrayCache.Default.Acquire();
                try
                {
                    var bufferLen = buffer.Length;

                    using (var reader = stream.NewReader(0))
                    {
                        int readLen;
                        while ((readLen = reader.Read(buffer, 0, bufferLen)) > 0)
                        {
                            Send(socket, buffer, readLen);
                        }
                    }
                }
                finally
                {
                    ByteArrayCache.Default.Release(buffer);
                }
            }
            return true;
        }

        protected virtual void Send(Socket socket, byte[] buffer, int bufferLen)
        {
            int sendLen;
            var offset = 0;
            var retryCount = 0;
            SocketError errorCode;

            while (offset < bufferLen)
            {
                try
                {
                    sendLen = socket.Send(buffer, offset, bufferLen - offset, SocketFlags.None, out errorCode);
                }
                catch (SocketException se)
                {
                    errorCode = se.SocketErrorCode;
                    if (errorCode != SocketError.WouldBlock)
                        throw;
                    sendLen = 0;
                }

                if (errorCode != SocketError.Success)
                {
                    if (errorCode == SocketError.WouldBlock)
                    {
                        if (sendLen > 0)
                        {
                            offset += sendLen;
                            continue;
                        }

                        if (retryCount++ < SendRetryTreshold)
                        {
                            if (retryCount > 1)
                                Thread.Sleep(retryCount > 50 ? 1 : 0);
                            continue;
                        }
                    }

                    throw new SocketException((int)errorCode);
                }

                offset += sendLen;
                if (retryCount > 0)
                    retryCount = 0;
            }
        }
    }
}
