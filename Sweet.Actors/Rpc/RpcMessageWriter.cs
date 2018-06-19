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

namespace Sweet.Actors
{
    internal class RpcMessageWriter : Disposable
    {
        private static readonly byte[] ProcessIdBytes = Common.ProcessId.ToBytes();

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

        public virtual bool Write(Stream stream, WireMessage wireMsg, bool flush = true)
        {
            ThrowIfDisposed();

            if (stream != null)
            {
                var data = _serializer.Serialize(wireMsg);
                if (data != null)
                {
                    var dataLen = data.Length;
                    if (dataLen > RpcConstants.MaxDataSize)
                        throw new Exception(Errors.MaxAllowedDataSizeExceeded);

                    /* Header */
                    // Header sign
                    stream.WriteByte(RpcConstants.HeaderSign);

                    // Process Id
                    stream.Write(ProcessIdBytes, 0, ProcessIdBytes.Length);

                    // Message Id
                    var msgIdBytes = Interlocked.Increment(ref _messageIdSeed).ToBytes();
                    stream.Write(msgIdBytes, 0, msgIdBytes.Length);

                    // Serialization type
                    stream.Write(_serializerKeyBytes, 0, _serializerKeyBytes.Length);

                    // Frame count
                    var frameCount = (ushort)(dataLen > 0 ? ((dataLen / RpcConstants.FrameDataSize) + 1) : 0);

                    var frameCntBytes = frameCount.ToBytes();
                    stream.Write(frameCntBytes, 0, frameCntBytes.Length);

                    var offset = 0;

                    /* Frames */
                    for (ushort frameIndex = 0; frameIndex < frameCount; frameIndex++)
                    {
                        // Frame sign
                        stream.WriteByte(RpcConstants.FrameSign);

                        // Process Id
                        stream.Write(ProcessIdBytes, 0, ProcessIdBytes.Length);

                        // Message Id
                        stream.Write(msgIdBytes, 0, msgIdBytes.Length);

                        // Frame Id
                        var frameIdBytes = frameIndex.ToBytes();
                        stream.Write(frameIdBytes, 0, frameIdBytes.Length);

                        // Frame length
                        var frameDataLen = (ushort)Math.Min(RpcConstants.FrameDataSize, dataLen - offset);

                        var frameDataLenBytes = frameDataLen.ToBytes();
                        stream.Write(frameDataLenBytes, 0, frameDataLenBytes.Length);

                        if (frameDataLen > 0)
                        {
                            stream.Write(data, offset, frameDataLen);
                            offset += frameDataLen;
                        }
                    }

                    if (flush)
                        stream.Flush();

                    return true;
                }
            }
            return false;
        }
    }
}
