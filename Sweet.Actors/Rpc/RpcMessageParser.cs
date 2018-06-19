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

            var bufferedLen = input?.Position ?? 0;
            if (bufferedLen >= RpcConstants.HeaderSize)
            {
                using (var reader = (IStreamReader)(input as ChunkedStream)?.NewReader() ?? new BinaryStreamReader(input))
                {
                    var mainHeaderBuffer = new byte[RpcConstants.HeaderSize];
                    var frameHeaderBuffer = new byte[RpcConstants.FrameHeaderSize];

                    reader.Read(mainHeaderBuffer, 0, RpcConstants.HeaderSize);

                    if (mainHeaderBuffer[0] != RpcConstants.HeaderSign)
                        throw new Exception(Errors.InvalidMessageType);

                    var headerIndex = sizeof(byte);

                    message = new RpcPartitionedMessage();

                    message.Header.ProcessId = mainHeaderBuffer.ToInt(headerIndex);
                    headerIndex += sizeof(int);

                    message.Header.MessageId = mainHeaderBuffer.ToInt(headerIndex);
                    headerIndex += sizeof(int);

                    var serializerKey =
                        Encoding.UTF8.GetString(mainHeaderBuffer, headerIndex, RpcConstants.SerializerRegistryNameLength)?.TrimEnd();
                    headerIndex += RpcConstants.SerializerRegistryNameLength;

                    if (serializerKey != null)
                    {
                        var pos = serializerKey.IndexOf('\0');
                        if (pos > -1)
                            serializerKey = serializerKey.Substring(0, pos);
                    }

                    if (String.IsNullOrEmpty(serializerKey))
                        serializerKey = Constants.DefaultSerializerKey;

                    message.Header.SerializerKey = serializerKey;

                    var frameCount = (message.Header.FrameCount = mainHeaderBuffer.ToUShort(headerIndex));

                    var completed = frameCount == 0;
                    if (!completed)
                    {
                        bufferedLen -= RpcConstants.HeaderSize;

                        if (bufferedLen < RpcConstants.FrameHeaderSize + (frameCount - 1) * RpcConstants.FrameSize)
                            return false;

                        try
                        {
                            for (var i = (ushort)0; i < frameCount; i++)
                            {
                                if (bufferedLen < RpcConstants.FrameHeaderSize)
                                    break;

                                reader.Read(frameHeaderBuffer, 0, RpcConstants.FrameHeaderSize);
                                bufferedLen -= RpcConstants.FrameHeaderSize;

                                if (frameHeaderBuffer[0] != RpcConstants.FrameSign)
                                    throw new Exception(Errors.InvalidMessageType);

                                var frame = new RpcPartitionedFrame();

                                headerIndex = sizeof(byte);

                                frame.ProcessId = frameHeaderBuffer.ToInt(headerIndex);
                                headerIndex += sizeof(int);

                                frame.MessageId = frameHeaderBuffer.ToInt(headerIndex);
                                headerIndex += sizeof(int);

                                if (frame.ProcessId != message.Header.ProcessId ||
                                    frame.MessageId != message.Header.MessageId)
                                    throw new Exception(RpcErrors.InvalidMessage);

                                frame.FrameId = frameHeaderBuffer.ToUShort(headerIndex);
                                headerIndex += sizeof(ushort);

                                var frameDataLen = frameHeaderBuffer.ToUShort(headerIndex);
                                headerIndex += sizeof(ushort);

                                if (frameDataLen > 0)
                                {
                                    if (bufferedLen < frameDataLen)
                                        break;

                                    bufferedLen -= frameDataLen;
                                    var dataBuffer = FrameCache.Acquire();

                                    var readLen = reader.Read(dataBuffer, 0, frameDataLen);
                                    if (readLen < frameDataLen)
                                        break;

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
                    return completed;
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
