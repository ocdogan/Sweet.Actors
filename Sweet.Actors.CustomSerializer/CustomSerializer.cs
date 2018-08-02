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
using System.Text;
using Wire;

namespace Sweet.Actors.Rpc
{
    public class CustomSerializer : IWireSerializer
    {
        private const byte NullFlag = 0;
        private const byte NotNullFlag = 1;

        private const int NullLengthFlag = -1;

        private static readonly WireMessage[] EmptyWireMessages = new WireMessage[] { };

        private Lazy<Serializer> _wireSerializer = 
            new Lazy<Serializer>(() => new Serializer(new SerializerOptions(versionTolerance: true, preserveObjectReferences: true)));

        public IEnumerable<WireMessage> Deserialize(byte[] data)
        {
            if (data == null || data.Length == 0)
                return EmptyWireMessages;

            using (var stream = new ChunkedStream(data))
                return DeserializeInternal(stream);
        }

        public IEnumerable<WireMessage> Deserialize(Stream stream)
        {
            if (stream != null && stream.CanRead)
                return DeserializeInternal(stream);
            return EmptyWireMessages;
        }

        private IEnumerable<WireMessage> DeserializeInternal(Stream stream)
        {
            using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
            {
                var b = reader.ReadByte();
                if (b == NullFlag)
                    yield return null;

                var count = reader.ReadInt32();
                if (count == 0)
                    yield return null;

                for (var i = 0; i < count; i++)
                    yield return Read(reader);
            }
        }

        public byte[] Serialize(WireMessage[] messages)
        {
            using (var stream = new ChunkedStream())
            {
                SerializeInternal(messages, stream);
                return stream.ToArray();
            }
        }

        public long Serialize(WireMessage[] messages, Stream stream)
        {
            if (stream != null && stream.CanWrite)
                return SerializeInternal(messages, stream);
            return -1L;
        }

        private long SerializeInternal(WireMessage[] messages, Stream stream)
        {
            if (messages == null)
            {
                stream.WriteByte(NullFlag);
                return 1L;
            }

            var previousPos = stream.Position;
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                writer.Write(NotNullFlag);

                var length = messages.Length;
                writer.Write(length);

                if (length > 0)
                {
                    for (var i = 0; i < length; i++)
                    {
                        Write(writer, messages[i]);
                    }
                }
            }

            return Math.Max(-1L, stream.Position - previousPos);
        }

        private WireMessage Read(BinaryReader reader)
        {
            var dataTypeCd = reader.ReadByte() - 1;
            if (dataTypeCd > 0)
            {
                var message = new WireMessage();

                message.MessageType = (MessageType)reader.ReadByte();
                message.State = (WireMessageState)reader.ReadByte();

                var timeoutMSec = reader.ReadInt32();
                message.TimeoutMSec = timeoutMSec != int.MinValue ? timeoutMSec : (int?)null;

                message.Id = WireMessageId.Read(reader);

                message.From = Aid.Parse(ReadString(reader));
                message.To = Aid.Parse(ReadString(reader));

                var h = reader.ReadByte();
                if (h != NullFlag)
                {
                    var header = new Dictionary<string, string>();
                    message.Header = header;

                    var count = reader.ReadInt32();
                    if (count > 0)
                    {
                        string key;
                        for (var i = 0; i < count; i++)
                        {
                            key = ReadString(reader);
                            if (key != null)
                                header[key] = ReadString(reader);
                        }
                    }
                }

                var len = reader.ReadInt32();
                if (len > 0)
                {
                    using (var tempStream = new ChunkedStream())
                    {
                        var chunkSize = tempStream.ChunkSize;
                        while (len > 0)
                        {
                            var bytes = reader.ReadBytes(chunkSize);

                            var readLen = bytes?.Length ?? 0;
                            if (readLen == 0)
                                throw new Exception(SerializationErrors.StreamNotContainingValidWireMessage);

                            len -= readLen;
                            tempStream.Write(bytes, 0, readLen);
                        }

                        message.Exception = _wireSerializer.Value.Deserialize<Exception>(tempStream);
                    }
                }

                ReadData(dataTypeCd, message, reader);

                return message;
            }
            return null;
        }

        private void ReadData(int dataTypeCd, WireMessage message, BinaryReader reader)
        {
            switch ((TypeCode)dataTypeCd)
            {
                case TypeCode.Boolean:
                    message.Data = reader.ReadBoolean();
                    break;
                case TypeCode.Byte:
                    message.Data = reader.ReadByte();
                    break;
                case TypeCode.Char:
                    message.Data = reader.ReadChar();
                    break;
                case TypeCode.DateTime:
                    message.Data = new DateTime(reader.ReadInt64());
                    break;
                case TypeCode.DBNull:
                    reader.ReadByte();
                    message.Data = DBNull.Value;
                    break;
                case TypeCode.Decimal:
                    message.Data = reader.ReadDecimal();
                    break;
                case TypeCode.Double:
                    message.Data = reader.ReadDouble();
                    break;
                case TypeCode.Empty:
                    reader.ReadByte();
                    break;
                case TypeCode.Int16:
                    message.Data = reader.ReadInt16();
                    break;
                case TypeCode.Int32:
                    message.Data = reader.ReadInt32();
                    break;
                case TypeCode.Int64:
                    message.Data = reader.ReadInt64();
                    break;
                case TypeCode.SByte:
                    message.Data = reader.ReadSByte();
                    break;
                case TypeCode.Single:
                    message.Data = reader.ReadSingle();
                    break;
                case TypeCode.String:
                    message.Data = ReadString(reader);
                    break;
                case TypeCode.UInt16:
                    message.Data = reader.ReadUInt16();
                    break;
                case TypeCode.UInt32:
                    message.Data = reader.ReadUInt32();
                    break;
                case TypeCode.UInt64:
                    message.Data = reader.ReadUInt64();
                    break;
                default:
                    {
                        var len = reader.ReadInt32();
                        if (len > 0)
                        {
                            using (var tempStream = new ChunkedStream())
                            {
                                var chunkSize = tempStream.ChunkSize;
                                while (len > 0)
                                {
                                    var bytes = reader.ReadBytes(chunkSize);

                                    var readLen = bytes?.Length ?? 0;
                                    if (readLen == 0)
                                        throw new Exception(SerializationErrors.StreamNotContainingValidWireMessage);

                                    len -= readLen;
                                    tempStream.Write(bytes, 0, readLen);
                                }

                                message.Data = _wireSerializer.Value.Deserialize(tempStream);
                            }
                        }
                    }
                    break;
            }
        }

        private string ReadString(BinaryReader reader)
        {
            var length = reader.ReadInt32();
            if (length == 0)
                return String.Empty;

            if (length > 0)
                return reader.ReadString();

            return null;
        }

        private static void ReadBytes(BinaryWriter writer, byte[] data)
        {
            var length = data?.Length ?? NullLengthFlag;

            writer.Write(length);
            if (length > 0)
                writer.Write(data);
        }

        private void Write(BinaryWriter writer, WireMessage message)
        {
            if (message is null)
            {
                writer.Write(NullFlag);
                return;
            }

            var dataTypeCd = Type.GetTypeCode(message.Data?.GetType());

            writer.Write((byte)(NotNullFlag + dataTypeCd));
            writer.Write((byte)message.MessageType);
            writer.Write((byte)message.State);

            writer.Write(message.TimeoutMSec ?? int.MinValue);

            (message.Id ?? WireMessageId.Empty).Write(writer);

            WriteString(writer, message.From?.ToString());
            WriteString(writer, message.To?.ToString());

            var header = message.Header;
            if (header == null)
                writer.Write(NullFlag);
            else
            {
                writer.Write(NotNullFlag);
                writer.Write(header.Count);

                foreach (var kv in header)
                {
                    WriteString(writer, kv.Key);
                    WriteString(writer, kv.Value);
                }
            }

            if (message.Exception is null)
                WriteBytes(writer, null);
            else
            {
                using (var dataStream = new ChunkedStream())
                {
                    _wireSerializer.Value.Serialize(message.Exception, dataStream);

                    dataStream.Position = 0L;

                    writer.Write((int)dataStream.Length);
                    dataStream.CopyTo(writer.BaseStream);
                }
            }

            WriteData(writer, dataTypeCd, message.Data);
        }

        private void WriteData(BinaryWriter writer, TypeCode dataTypeCd, object data)
        {
            switch (dataTypeCd)
            {
                case TypeCode.Boolean:
                    writer.Write((bool)data);
                    break;
                case TypeCode.Byte:
                    writer.Write((byte)data);
                    break;
                case TypeCode.Char:
                    writer.Write((char)data);
                    break;
                case TypeCode.DateTime:
                    writer.Write(((DateTime)data).Ticks);
                    break;
                case TypeCode.DBNull:
                    writer.Write(NullFlag);
                    break;
                case TypeCode.Decimal:
                    writer.Write((decimal)data);
                    break;
                case TypeCode.Double:
                    writer.Write((double)data);
                    break;
                case TypeCode.Empty:
                    writer.Write(NullFlag);
                    break;
                case TypeCode.Int16:
                    writer.Write((short)data);
                    break;
                case TypeCode.Int32:
                    writer.Write((int)data);
                    break;
                case TypeCode.Int64:
                    writer.Write((long)data);
                    break;
                case TypeCode.SByte:
                    writer.Write((sbyte)data);
                    break;
                case TypeCode.Single:
                    writer.Write((float)data);
                    break;
                case TypeCode.String:
                    WriteString(writer, (string)data);
                    break;
                case TypeCode.UInt16:
                    writer.Write((ushort)data);
                    break;
                case TypeCode.UInt32:
                    writer.Write((uint)data);
                    break;
                case TypeCode.UInt64:
                    writer.Write((ulong)data);
                    break;
                default:
                    {
                        using (var dataStream = new ChunkedStream())
                        {
                            _wireSerializer.Value.Serialize(data, dataStream);

                            dataStream.Position = 0L;

                            writer.Write((int)dataStream.Length);
                            dataStream.CopyTo(writer.BaseStream);
                        }
                    }
                    break;
            }
        }

        private static void WriteString(BinaryWriter writer, string data)
        {
            var length = data?.Length ?? NullLengthFlag;

            writer.Write(length);
            if (length > 0)
                writer.Write(data);
        }

        private static void WriteBytes(BinaryWriter writer, byte[] data)
        {
            var length = data?.Length ?? NullLengthFlag;

            writer.Write(length);
            if (length > 0)
                writer.Write(data);
        }
    }
}