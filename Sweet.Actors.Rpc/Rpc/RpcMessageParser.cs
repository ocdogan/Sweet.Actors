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
    internal static class RpcMessageParser
    {
        private class ParserContext
        {
            public bool Completed;
            public long InputLength;
            public int StreamOffset;
            public byte[] HeaderBuffer;
            public IStreamReader StreamReader;
            public RpcPartitionedMessage Message;
        }

        private static readonly ByteArrayCache FrameCache = 
            new ByteArrayCache(10, -1, RpcConstants.FrameDataSize);

        private static readonly ByteArrayCache HeaderCache = 
            new ByteArrayCache(5, -1, Math.Max(RpcConstants.HeaderSize, RpcConstants.FrameHeaderSize));

        private static string _serializerKey;
        private static IWireSerializer _serializer;
        private static readonly ReaderWriterLockSlim _serializerLock = new ReaderWriterLockSlim();

        public static bool TryParse(Stream input, out RemoteMessage[] messages)
        {
            messages = null;
            if (!TryParsePartitioned(input, out RpcPartitionedMessage partitionedMsg))
                return false;

            try
            {
                var frames = partitionedMsg.Frames;
                if (frames != null)
                {
                    var frameCount = frames.Count;
                    if (frameCount > 0)
                    {
                        var serializer = GetSerializer(partitionedMsg);
                        if (serializer == null)
                            throw new Exception(RpcErrors.InvalidSerializerKey);

                        var frameDataList = new List<ArraySegment<byte>>(frameCount);
                        foreach (var frame in frames)
                        {
                            if (frame?.Data is byte[] data)
                            {
                                var dataLen = data.Length;
                                if (dataLen > 0)
                                {
                                    var frameDataLen = frame.DataLength;

                                    frameDataList.Add((frameDataLen == dataLen) ?
                                        new ArraySegment<byte>(data) :
                                        new ArraySegment<byte>(data, 0, frameDataLen));
                                }
                            }
                        }

                        using (var stream = new ChunkedStream(frameDataList))
                        {
                            messages = serializer.Deserialize(stream).ToArray();
                            return ((messages?.Length ?? 0) > 0);
                        }
                    }
                }
            }
            finally
            {
                ReleaseFrames(partitionedMsg);
            }
            return false;
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

        private static bool TryParsePartitioned(Stream input, out RpcPartitionedMessage message)
        {
            message = null;

            var context = new ParserContext {
                InputLength = input?.Length ?? 0
            };

            if (context.InputLength < RpcConstants.HeaderSize)
                return false;

            context.HeaderBuffer = HeaderCache.Acquire();
            var chunkedStream = input as ChunkedStream;
            try
            {
                context.Message = new RpcPartitionedMessage();

                using (context.StreamReader = (IStreamReader)chunkedStream?.NewReader() ??
                                        new BinaryStreamReader(input))
                {
                    if (!ParseHeader(context))
                        return false;

                    if (!context.Completed)
                    {
                        try
                        {
                            var frameCount = context.Message.Header.FrameCount;
                            for (var i = (ushort)0; i < frameCount; i++)
                                if (!ParseFrame(context))
                                    return false;

                            context.Completed = true;
                        }
                        catch (Exception)
                        {
                            ReleaseFrames(context.Message);
                            throw;
                        }
                    }
                }

                if (context.Completed)
                {
                    chunkedStream?.TrimLeft(context.StreamOffset);

                    message = context.Message;
                    return true;
                }
            }
            finally
            {
                HeaderCache.Release(context.HeaderBuffer);
            }
            return false;
        }

        private static bool ParseHeader(ParserContext context)
        {
            if (context.InputLength >= RpcConstants.HeaderSize)
            {
                context.StreamReader.Read(context.HeaderBuffer, 0, RpcConstants.HeaderSize);

                if (context.HeaderBuffer[0] != RpcConstants.HeaderSign)
                    throw new Exception(Errors.InvalidMessageType);

                var offset = sizeof(byte);
                var header = context.Message.Header;

                header = context.Message.Header;
                header.ProcessId = context.HeaderBuffer.ToInt(offset);
                offset += sizeof(int);

                header.MessageId = context.HeaderBuffer.ToInt(offset);
                offset += sizeof(int);

                header.SerializerKey = GetSerializerKey(context.HeaderBuffer, offset);
                offset += RpcConstants.SerializerRegistryNameLength;

                header.FrameCount = context.HeaderBuffer.ToUShort(offset);
                offset += sizeof(ushort);

                context.InputLength -= offset;
                context.StreamOffset += offset;

                if (header.FrameCount == 0)
                    context.Completed = true;
                else
                {
                    if (context.InputLength <= 0)
                        return false;

                    var maxFramesDataSize = RpcConstants.FrameHeaderSize + 
                        ((context.Message.Header.FrameCount - 1) * RpcConstants.FrameSize);

                    if (context.InputLength < maxFramesDataSize)
                        return false;
                }

                return true;
            }
            return false;
        }
        
        private static bool ParseFrame(ParserContext context)
        {
            if (!ParseFrameHeader(context, out RpcPartitionedFrame frame))
                return false;

            var frameDataLen = frame.DataLength;
            if (frameDataLen > 0)
            {
                if (context.InputLength < frameDataLen)
                    return false;

                context.InputLength -= frameDataLen;
                var dataBuffer = FrameCache.Acquire();

                var readLen = context.StreamReader.Read(dataBuffer, 0, frameDataLen);
                if (readLen < frameDataLen)
                    return false;

                frame.Data = dataBuffer;
                frame.DataLength = frameDataLen;

                context.InputLength -= frameDataLen;
                context.StreamOffset += frameDataLen;
            }

            if (frame != null)
                context.Message.Frames.Add(frame);

            return true;
        }

        private static bool ParseFrameHeader(ParserContext context, out RpcPartitionedFrame frame)
        {
            frame = null;
            if (context.InputLength < RpcConstants.FrameHeaderSize)
                return false;

            context.StreamReader.Read(context.HeaderBuffer, 0, RpcConstants.FrameHeaderSize);

            if (context.HeaderBuffer[0] != RpcConstants.FrameSign)
                throw new Exception(Errors.InvalidMessageType);

            frame = new RpcPartitionedFrame();

            var offset = sizeof(byte);

            frame.ProcessId = context.HeaderBuffer.ToInt(offset);
            offset += sizeof(int);

            if (frame.ProcessId != context.Message.Header.ProcessId)
                throw new Exception(RpcErrors.InvalidMessage);

            frame.MessageId = context.HeaderBuffer.ToInt(offset);
            offset += sizeof(int);

            if (frame.MessageId != context.Message.Header.MessageId)
                throw new Exception(RpcErrors.InvalidMessage);

            frame.FrameId = context.HeaderBuffer.ToUShort(offset);
            offset += sizeof(ushort);

            frame.DataLength = context.HeaderBuffer.ToUShort(offset);
            offset += sizeof(ushort);

            context.InputLength -= offset;
            context.StreamOffset += offset;

            return true;
        }

        private static string GetSerializerKey(byte[] buffer, int offset)
        {
            var result = Encoding.UTF8.GetString(buffer, offset, RpcConstants.SerializerRegistryNameLength)?.TrimEnd();

            var nullTerminationPos = result?.IndexOf('\0') ?? -1;
            if (nullTerminationPos < 1)
                return Constants.DefaultSerializerKey;

            result = result.Substring(0, nullTerminationPos)?.TrimEnd();
            return !String.IsNullOrEmpty(result) ? result : 
                Constants.DefaultSerializerKey;
        }

        private static void ReleaseFrames(RpcPartitionedMessage message)
        {
            var frames = message?.Frames;
            if (frames != null)
            {
                var frameCount = frames.Count;
                for (var i = 0; i < frameCount; i++)
                    FrameCache.Release(frames[i].Data);
            }
        }
    }
}
