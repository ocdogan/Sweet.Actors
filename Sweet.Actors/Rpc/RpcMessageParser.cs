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

namespace Sweet.Actors
{
    internal class RpcMessageParser
    {
        private static readonly ByteArrayCache FrameCache = new ByteArrayCache(10, -1, RpcConstants.FrameDataSize);

        public bool TryParse(Stream input, out RpcPartitionedMessage message)
        {
            message = null;

            var inputLen = input?.Length ?? 0;
            if (inputLen < RpcConstants.HeaderSize)
                return false;

            ChunkedStream chunkedStream;
            using (var reader = (IStreamReader)(chunkedStream = input as ChunkedStream)?.NewReader() ?? new BinaryStreamReader(input))
            {
                var headerBuffer = new byte[RpcConstants.HeaderSize];

                reader.Read(headerBuffer, 0, RpcConstants.HeaderSize);

                var streamOffset = RpcConstants.HeaderSize;

                if (headerBuffer[0] != RpcConstants.HeaderSign)
                    throw new Exception(Errors.InvalidMessageType);

                var bufferOffset = sizeof(byte);

                message = new RpcPartitionedMessage();

                message.Header.ProcessId = headerBuffer.ToInt(bufferOffset);
                bufferOffset += sizeof(int);

                message.Header.MessageId = headerBuffer.ToInt(bufferOffset);
                bufferOffset += sizeof(int);

                var serializerKey =
                    Encoding.UTF8.GetString(headerBuffer, bufferOffset, RpcConstants.SerializerRegistryNameLength)?.TrimEnd();
                bufferOffset += RpcConstants.SerializerRegistryNameLength;

                if (serializerKey != null)
                {
                    var pos = serializerKey.IndexOf('\0');
                    if (pos > -1)
                        serializerKey = serializerKey.Substring(0, pos);
                }

                if (String.IsNullOrEmpty(serializerKey))
                    serializerKey = Constants.DefaultSerializerKey;

                message.Header.SerializerKey = serializerKey;

                var frameCount = (message.Header.FrameCount = headerBuffer.ToUShort(bufferOffset));

                var completed = frameCount == 0;
                if (!completed)
                {
                    inputLen -= RpcConstants.HeaderSize;

                    if (inputLen < RpcConstants.FrameHeaderSize + (frameCount - 1) * RpcConstants.FrameSize)
                        return false;

                    try
                    {
                        headerBuffer = new byte[RpcConstants.FrameHeaderSize];

                        for (var i = (ushort)0; i < frameCount; i++)
                        {
                            if (inputLen < RpcConstants.FrameHeaderSize)
                                break;

                            reader.Read(headerBuffer, 0, RpcConstants.FrameHeaderSize);
                            inputLen -= RpcConstants.FrameHeaderSize;

                            streamOffset += RpcConstants.FrameHeaderSize;

                            if (headerBuffer[0] != RpcConstants.FrameSign)
                                throw new Exception(Errors.InvalidMessageType);

                            var frame = new RpcPartitionedFrame();

                            bufferOffset = sizeof(byte);

                            frame.ProcessId = headerBuffer.ToInt(bufferOffset);
                            bufferOffset += sizeof(int);

                            frame.MessageId = headerBuffer.ToInt(bufferOffset);
                            bufferOffset += sizeof(int);

                            if (frame.ProcessId != message.Header.ProcessId ||
                                frame.MessageId != message.Header.MessageId)
                                throw new Exception(RpcErrors.InvalidMessage);

                            frame.FrameId = headerBuffer.ToUShort(bufferOffset);
                            bufferOffset += sizeof(ushort);

                            var frameDataLen = headerBuffer.ToUShort(bufferOffset);
                            bufferOffset += sizeof(ushort);

                            if (frameDataLen > 0)
                            {
                                if (inputLen < frameDataLen)
                                    break;

                                inputLen -= frameDataLen;
                                var dataBuffer = FrameCache.Acquire();

                                var readLen = reader.Read(dataBuffer, 0, frameDataLen);
                                if (readLen < frameDataLen)
                                    break;

                                streamOffset += frameDataLen;

                                frame.Data = dataBuffer;
                                frame.DataLength = frameDataLen;
                            }

                            message.Frames.Add(frame);

                            if (i == frameCount - 1)
                                completed = true;
                        }
                    }
                    catch (Exception)
                    {
                        ReleaseFrames(message);
                        throw;
                    }
                }

                if (completed)
                {
                    if (chunkedStream != null)
                        chunkedStream.TrimLeft(streamOffset);
                    return true;
                }
            }
            return false;
        }

        private static void ReleaseFrames(RpcPartitionedMessage message)
        {
            if (message != null)
            {
                var frames = message.Frames;
                if (frames != null)
                {
                    var frameCnt = frames.Count;
                    for (var i = 0; i < frameCnt; i++)
                        FrameCache.Release(frames[i].Data);
                }
            }
        }
    }
}
