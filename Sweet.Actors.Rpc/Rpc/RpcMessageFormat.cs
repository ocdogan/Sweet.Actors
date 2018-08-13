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

namespace Sweet.Actors.Rpc
{
    public static class RpcHeaderSizeOf
    {
        public const int Sign = sizeof(byte);
        public const int ProcessId = sizeof(int);
        public const int MessageId = sizeof(int);
        public const int SerializerKey = 10;
        public const int DataSize = sizeof(int);
    }

    public static class RpcHeaderOffsetOf
    {
        public const int Sign = 0;
        public const int ProcessId = Sign + RpcHeaderSizeOf.Sign;
        public const int MessageId = ProcessId + RpcHeaderSizeOf.ProcessId;
        public const int SerializerKey = MessageId + RpcHeaderSizeOf.MessageId;
        public const int DataSize = SerializerKey + RpcHeaderSizeOf.SerializerKey;
    }

    public static class RpcMessageSizeOf
    {
        public const int Header =
            RpcHeaderSizeOf.Sign   /* Header sign (byte) */
            + RpcHeaderSizeOf.ProcessId /* Process id (int) */
            + RpcHeaderSizeOf.MessageId /* Message id (int) */
            + RpcHeaderSizeOf.SerializerKey /* Serializer registry name length (byte[]) */
            + RpcHeaderSizeOf.DataSize /* DataSize (int) */;

        public const int MaxAllowedData = 4 * Constants.MB; // 4 MByte
    }

    public static class RpcMessageSign
    {
        public const byte Frame = (byte)'~';
        public const byte Header = (byte)'*';
    }
}
