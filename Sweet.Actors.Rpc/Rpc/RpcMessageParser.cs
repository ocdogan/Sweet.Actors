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
            new ByteArrayCache(10, -1, RpcMessageSizeOf.EachFrameData);

        private static readonly ByteArrayCache HeaderCache = 
            new ByteArrayCache(5, -1, Math.Max(RpcMessageSizeOf.Header, RpcMessageSizeOf.FrameHeader));

        private static string _serializerKey;
        private static IWireSerializer _serializer;
        private static readonly ReaderWriterLockSlim _serializerLock = new ReaderWriterLockSlim();

        public static bool TryParse(Stream input, out IEnumerable<RemoteMessage> messages)
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
                            var list = serializer.Deserialize(stream).ToList();
                            messages = list;

                            return ((list?.Count ?? 0) > 0);
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

            if (context.InputLength < RpcMessageSizeOf.Header)
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
            if (context.InputLength >= RpcMessageSizeOf.Header)
            {
                context.StreamReader.Read(context.HeaderBuffer, 0, RpcMessageSizeOf.Header);

                if (context.HeaderBuffer[0] != RpcMessageSign.Header)
                    throw new Exception(Errors.InvalidMessageType);

                var header = context.Message.Header;

                header.ProcessId = context.HeaderBuffer.ToInt(RpcHeaderOffsetOf.ProcessId);
                header.MessageId = context.HeaderBuffer.ToInt(RpcHeaderOffsetOf.MessageId);
                header.SerializerKey = GetSerializerKey(context.HeaderBuffer, RpcHeaderOffsetOf.SerializerKey);
                header.FrameCount = context.HeaderBuffer.ToUShort(RpcHeaderOffsetOf.FrameCount);

                context.InputLength -= RpcMessageSizeOf.Header;
                context.StreamOffset += RpcMessageSizeOf.Header;

                if (header.FrameCount == 0)
                    context.Completed = true;
                else
                {
                    if (context.InputLength <= 0)
                        return false;

                    var maxFramesDataSize = RpcMessageSizeOf.FrameHeader + 
                        ((header.FrameCount - 1) * RpcMessageSizeOf.EachFrame);

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

            if (frame != null)
            {
                var frameDataLen = frame.DataLength;
                if (frameDataLen > 0)
                {
                    if (context.InputLength < frameDataLen)
                        return false;

                    var dataBuffer = FrameCache.Acquire();

                    var readLen = context.StreamReader.Read(dataBuffer, 0, frameDataLen);
                    if (readLen < frameDataLen)
                        return false;

                    frame.Data = dataBuffer;
                    frame.DataLength = frameDataLen;

                    context.InputLength -= frameDataLen;
                    context.StreamOffset += frameDataLen;
                }

                context.Message.Frames.Add(frame);
            }
            return true;
        }

        private static bool ParseFrameHeader(ParserContext context, out RpcPartitionedFrame frame)
        {
            frame = null;
            if (context.InputLength < RpcMessageSizeOf.FrameHeader)
                return false;

            context.StreamReader.Read(context.HeaderBuffer, 0, RpcMessageSizeOf.FrameHeader);

            if (context.HeaderBuffer[0] != RpcMessageSign.Frame)
                throw new Exception(Errors.InvalidMessageType);

            frame = new RpcPartitionedFrame();

            var offset = sizeof(byte);

            frame.ProcessId = context.HeaderBuffer.ToInt(RpcFrameHeaderOffsetOf.ProcessId);
            offset += sizeof(int);

            var header = context.Message.Header;

            if (frame.ProcessId != header.ProcessId)
                throw new Exception(RpcErrors.InvalidMessage);

            frame.MessageId = context.HeaderBuffer.ToInt(RpcFrameHeaderOffsetOf.MessageId);
            offset += sizeof(int);

            if (frame.MessageId != header.MessageId)
                throw new Exception(RpcErrors.InvalidMessage);

            frame.FrameId = context.HeaderBuffer.ToUShort(RpcFrameHeaderOffsetOf.FrameId);
            offset += sizeof(ushort);

            frame.DataLength = context.HeaderBuffer.ToUShort(RpcFrameHeaderOffsetOf.FrameData);
            offset += sizeof(ushort);

            context.InputLength -= offset;
            context.StreamOffset += offset;

            return true;
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
