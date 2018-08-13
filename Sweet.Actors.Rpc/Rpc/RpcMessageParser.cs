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
using System.Linq;
using System.Text;
using System.Threading;

namespace Sweet.Actors.Rpc
{
    public static class RpcMessageParser
    {
        private class ParserContext
        {
            public bool Completed;
            public long InputLength;
            public int StreamOffset;
            public byte[] HeaderBuffer;

            public ChunkedStream Input;
            public RpcPartitionedMessage Message;
        }

        private static string _serializerKey;
        private static IWireSerializer _serializer;
        private static readonly ReaderWriterLockSlim _serializerLock = new ReaderWriterLockSlim();

        public static bool TryParse(ChunkedStream input, out IEnumerable<WireMessage> messages)
        {
            messages = null;
            if (!TryParsePartitioned(input, out RpcPartitionedMessage message))
                return false;

            var stream = message.Data;
            if (stream != null)
            {
                using (stream)
                {
                    var serializer = GetSerializer(message);

                    stream.Position = 0;
                    messages = serializer.Deserialize(stream);
                }
            }
            return true;
        }

        private static IWireSerializer GetSerializer(RpcPartitionedMessage message)
        {
            IWireSerializer result = null;

            var serializerKey = message.Header.SerializerKey;
            if (String.IsNullOrEmpty(serializerKey))
                serializerKey = Constants.DefaultSerializerKey;

            _serializerLock.EnterUpgradeableReadLock();
            try
            {
                if (serializerKey == _serializerKey)
                    result = _serializer;
                else
                {
                    _serializerLock.EnterWriteLock();
                    try
                    {
                        _serializer = (result = RpcSerializerRegistry.Get(serializerKey));
                        _serializerKey = serializerKey;
                    }
                    finally
                    {
                        _serializerLock.ExitWriteLock();
                    }
                }
            }
            finally
            {
                _serializerLock.ExitUpgradeableReadLock();
            }
            return result;
        }

        private static bool TryParsePartitioned(ChunkedStream input, out RpcPartitionedMessage message)
        {
            message = null;
            if (input == null || !input.CanRead)
                return false;

            var inputDataLen = input.Length;
            if (inputDataLen < RpcMessageSizeOf.Header)
                return false;

            var context = new ParserContext
            {
                Input = input,
                InputLength = inputDataLen,
                HeaderBuffer = RpcByteBufferCache.HeaderCache.Acquire()
            };

            try
            {
                context.Message = new RpcPartitionedMessage();

                var initialPosition = input.Position;
                input.Position = 0;
                try
                {
                    if (!ParseHeader(context))
                        return false;

                    if (!ParseData(context))
                        return false;
                }
                finally
                {
                    input.Position = initialPosition;
                }

                if (context.Completed)
                {
                    message = context.Message;
                    input.TrimLeft(context.StreamOffset);

                    return true;
                }
            }
            finally
            {
                RpcByteBufferCache.HeaderCache.Release(context.HeaderBuffer);
            }

            return false;
        }

        private static bool ParseHeader(ParserContext context)
        {
            if (context.InputLength < RpcMessageSizeOf.Header)
                return false;

            var buffer = context.HeaderBuffer;

            var readLen = context.Input.Read(buffer, 0, RpcMessageSizeOf.Header);
            if (readLen != RpcMessageSizeOf.Header)
                return false;

            if (buffer[0] != RpcMessageSign.Header)
                throw new Exception(Errors.InvalidMessageType);

            var header = context.Message.Header;

            header.ProcessId = buffer.ToInt(RpcHeaderOffsetOf.ProcessId);
            header.MessageId = buffer.ToInt(RpcHeaderOffsetOf.MessageId);
            header.SerializerKey = GetSerializerKey(buffer, RpcHeaderOffsetOf.SerializerKey);
            header.DataSize = buffer.ToInt(RpcHeaderOffsetOf.DataSize);

            context.InputLength -= RpcMessageSizeOf.Header;
            context.StreamOffset += RpcMessageSizeOf.Header;

            return true;
        }

        private static bool ParseData(ParserContext context)
        {
            if (context.InputLength <= 0)
                return false;

            var ctxMessage = context.Message;

            var dataLen = ctxMessage.Header.DataSize;
            if (dataLen < 0)
                throw new Exception(RpcErrors.InvalidMessage);

            if (dataLen == 0)
            {
                context.Completed = true;
                return true;
            }

            if (context.InputLength >= dataLen)
            {
                var dataStream = new ChunkedStream();
                try
                {
                    var readLen = dataStream.ReadFrom(context.Input, dataLen);
                    if (readLen < dataLen)
                    {
                        dataStream.Dispose();
                        return false;
                    }

                    context.InputLength -= dataLen;
                    context.StreamOffset += dataLen;

                    ctxMessage.Data = dataStream;

                    context.Completed = true;
                    return true;
                }
                catch (Exception)
                {
                    dataStream.Dispose();
                }
            }
            return false;
        }
        

        private static string GetSerializerKey(byte[] buffer, int offset)
        {
            var result = Encoding.UTF8.GetString(buffer, offset, RpcHeaderSizeOf.SerializerKey)?.TrimEnd();

            var nullTerminationPos = result?.IndexOf('\0') ?? -1;
            if (nullTerminationPos < 1)
                return Constants.DefaultSerializerKey;

            result = result.Substring(0, nullTerminationPos)?.TrimEnd();
            return !String.IsNullOrEmpty(result) ? result : 
                Constants.DefaultSerializerKey;
        }
    }
}
