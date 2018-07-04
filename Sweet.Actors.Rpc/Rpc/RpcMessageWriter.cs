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
    internal class RpcMessageWriter : Disposable
    {
        private static readonly byte[] ProcessIdBytes = Common.ProcessId.ToBytes();

        private static ArraySliceCache SliceCache = new ArraySliceCache(initialCount: 20, arraySize: RpcConstants.FrameSize);

        private int _messageIdSeed;
        private IWireSerializer _serializer;
        private byte[] _serializerKeyBytes = new byte[RpcConstants.SerializerRegistryNameLength];

        public RpcMessageWriter(string serializerKey)
        {
            InitializeSerializer(serializerKey);
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

        protected virtual void WriteHeader(Stream outStream, long dataLen, out byte[] msgIdBytes)
        {
            outStream.WriteByte(RpcConstants.HeaderSign);

            // Process Id
            outStream.Write(ProcessIdBytes, 0, ProcessIdBytes.Length);

            // Message Id
            msgIdBytes = Interlocked.Increment(ref _messageIdSeed).ToBytes();
            outStream.Write(msgIdBytes, 0, msgIdBytes.Length);

            // Serialization type
            outStream.Write(_serializerKeyBytes, 0, _serializerKeyBytes.Length);

            // Frame count
            var frameCount = (ushort)(dataLen > 0 ? ((dataLen / RpcConstants.FrameDataSize) + 1) : 0);

            var frameCntBytes = frameCount.ToBytes();
            outStream.Write(frameCntBytes, 0, frameCntBytes.Length);
        }

        public virtual bool Write(Stream outStream, WireMessage message, bool flush = true)
        {
            ThrowIfDisposed();

            if (outStream == null)
                return false;

            var dataStream = new ChunkedStream();
            try
            {
                var dataLen = _serializer.Serialize(message, dataStream);
                if (dataLen > RpcConstants.MaxDataSize)
                    throw new Exception(Errors.MaxAllowedDataSizeExceeded);

                /* Header */
                WriteHeader(outStream, dataLen, out byte[] messageId);

                // Frame count
                if (dataLen > 0)
                {
                    var offset = 0;
                    var frameCount = (ushort)(dataLen > 0 ? ((dataLen / RpcConstants.FrameDataSize) + 1) : 0);

                    using (var slice = SliceCache.Acquire())
                    {
                        var buffer = slice.Array;

                        for (ushort frameIndex = 0; frameIndex < frameCount; frameIndex++)
                        {
                            // Frame sign
                            outStream.WriteByte(RpcConstants.FrameSign);

                            // Process Id
                            outStream.Write(ProcessIdBytes, 0, ProcessIdBytes.Length);

                            // Message Id
                            outStream.Write(messageId, 0, messageId.Length);

                            // Frame Id
                            var frameIdBytes = frameIndex.ToBytes();
                            outStream.Write(frameIdBytes, 0, frameIdBytes.Length);

                            // Frame length
                            var frameDataLen = (ushort)Math.Min(RpcConstants.FrameDataSize, dataLen - offset);
                            dataStream.Read(slice, 0, frameDataLen);

                            var frameDataLenBytes = frameDataLen.ToBytes();
                            outStream.Write(frameDataLenBytes, 0, frameDataLenBytes.Length);

                            if (frameDataLen > 0)
                            {
                                outStream.Write(buffer, offset, frameDataLen);
                                offset += frameDataLen;
                            }
                        }
                    }
                }

                if (flush)
                    outStream.Flush();
            }
            finally
            {
                AsyncEventPool.Run(dataStream.Dispose);
            }
            return true;
        }

        public virtual bool Write(Socket socket, WireMessage message)
        {
            ThrowIfDisposed();

            if (socket.IsConnected())
            {
                var outStream = new ChunkedStream();
                try
                {
                    if (Write(outStream, message))
                    {
                        using (var slice = SliceCache.Acquire())
                        {
                            var buffer = slice.Array;

                            int readLen;
                            while ((readLen = outStream.Read(buffer, 0, buffer.Length)) > 0)
                                socket.Send(buffer, 0, readLen, SocketFlags.None);
                        }
                        return true;
                    }
                }
                finally
                {
                    AsyncEventPool.Run(outStream.Dispose);
                }
            }
            return false;
        }
    }
}
