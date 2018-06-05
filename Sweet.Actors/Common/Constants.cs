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

using System.Globalization;

namespace Sweet.Actors
{
    public static class Constants
    {
		public const int True = 1;
        public const int False = 0;

        public const int KB = 1024;
        public const int MB = KB * KB;
        public const int GB = KB * MB;

        public const int AnyAvailablePort = 0;
        public const int DefaultPort = 6663;
        public const string DefaultHost = "local";

        public const string Protocol = "playbook://";
        public const string AddressFormat = "playbook://{0}:{1}/{2}";

        public const string EmptyActorName = "[FAE04EC0-301F-11D3-BF4B-00C04F79EFBC]";
        public const string DefaultActorSystemName = "[EDF53DA8-5449-4489-BDFC-CF165071362A]";

        public const int MinSequentialInvokeLimit = 50;
        public const int MaxSequentialInvokeLimit = 2000;
        public const int DefaultSequentialInvokeLimit = 240;

        public static readonly int ProtocolLength = Protocol.Length;
        public static readonly int EmptyProtocolLength = "playbook://:0/".Length;

        public const string LocalHost = "localhost";
        public const string IP4Loopback = "127.0.0.1";
        public const string IP6Loopback = "::1";

        public const int SIO_LOOPBACK_FAST_PATH = -1744830448;

        public const int DefaultSendTimeout = 15000;
        public const int MinSendTimeout = 100;
        public const int MaxSendTimeout = 60000;

        public const int DefaultReceiveTimeout = 15000;
        public const int MinReceiveTimeout = 100;
        public const int MaxReceiveTimeout = 60000;

        public const int WriteBufferSize = 64 * KB;

        public const byte FrameSign = (byte)'~';
        public const byte HeaderSign = (byte)'*';

        public const int HeaderSize = 1 /* Header sign (byte) */ 
            - 4 /* Process id (int) */ - 4 /* Message id (int) */ - 2 /* Frame count (ushort) */;

        public const int FrameSize = 8 * Constants.KB; // 8 KByte
        public const int FrameDataSize = FrameSize - 1 /* Frame sign (byte) */ 
            - 4 /* Process id (int) */ - 4 /* Message id (int) */ - 2 /* Frame id (ushort) */ 
            - 2 /* Frame data size (ushort) */;

        public const int MaxDataSize = 4 * Constants.MB; // 4 MByte
        public const int MaxFrameCount = (MaxDataSize / FrameDataSize) + 1;

        public static readonly CultureInfo InvariantCulture = CultureInfo.InvariantCulture;
    }
}
