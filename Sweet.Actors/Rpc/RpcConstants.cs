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

namespace Sweet.Actors
{
    public static class RpcConstants
    {
        public const int DefaultSendTimeout = 15000;
        public const int MinSendTimeout = 100;
        public const int MaxSendTimeout = 60000;

        public const int DefaultReceiveTimeout = 15000;
        public const int MinReceiveTimeout = 100;
        public const int MaxReceiveTimeout = 60000;

        public const int DefaultConnectionTimeout = 15000;
        public const int MinConnectionTimeout = 100;
        public const int MaxConnectionTimeout = 30000;

        public const int WriteBufferSize = 64 * Constants.KB;

        public const byte FrameSign = (byte)'~';
        public const byte HeaderSign = (byte)'*';

        public const int SerializerRegistryNameLength = 10;

        public const int HeaderSize = 
            1   /* Header sign (byte) */ 
            + 4 /* Process id (int) */ 
            + 4 /* Message id (int) */ 
            + SerializerRegistryNameLength /* Serializer registry name length (byte[]) */ 
            + 2 /* Frame count (ushort) */;

        public const int FrameSize = 8 * Constants.KB; // 8 KByte

        public const int FrameHeaderSize = 
            1   /* Frame sign (byte) */ 
            + 4 /* Process id (int) */ 
            + 4 /* Message id (int) */ 
            + 2 /* Frame id (ushort) */ 
            + 2 /* Frame data size (ushort) */;
        public const int FrameDataSize = FrameSize - FrameHeaderSize;

        public const int MaxDataSize = 4 * Constants.MB; // 4 MByte
        public const int MaxFrameCount = (MaxDataSize / FrameDataSize) + 1;
    }
}