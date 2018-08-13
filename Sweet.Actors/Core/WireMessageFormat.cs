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
    public static class WireMessageFieldSizeOf
    {
        public const int DataTypeCd = sizeof(byte);
        public const int MessageType = sizeof(byte);
        public const int State = sizeof(byte);
        public const int TimeoutMSec = sizeof(int);

        public const int IdMajor = sizeof(int);
        public const int IdMajorRevision = sizeof(int);
        public const int IdMinor = sizeof(int);
        public const int IdMinorRevision = sizeof(int);
        public const int IdProcessId = sizeof(int);

        public const int Id = IdMajor +
            IdMajorRevision +
            IdMinor +
            IdMinorRevision +
            IdProcessId;
    }

    public static class WireMessageBufferOffsetOf
    {
        public const int DataTypeCd = 0;
        public const int MessageType = DataTypeCd + WireMessageFieldSizeOf.DataTypeCd;
        public const int State = MessageType + WireMessageFieldSizeOf.MessageType;
        public const int TimeoutMSec = State + WireMessageFieldSizeOf.State;
        public const int Id = TimeoutMSec + WireMessageFieldSizeOf.TimeoutMSec;

        public const int IdMajor = TimeoutMSec + WireMessageFieldSizeOf.TimeoutMSec;
        public const int IdMajorRevision = IdMajor + WireMessageFieldSizeOf.IdMajor;
        public const int IdMinor = IdMajorRevision + WireMessageFieldSizeOf.IdMajorRevision;
        public const int IdMinorRevision = IdMinor + WireMessageFieldSizeOf.IdMinor;
        public const int IdProcessId = IdMinorRevision + WireMessageFieldSizeOf.IdMinorRevision;
    }

    public static class WireMessageSizeOf
    {
        public const int ConstantFields =
            WireMessageFieldSizeOf.DataTypeCd   /* Data type code (byte) */
            + WireMessageFieldSizeOf.MessageType /* Message type (byte) */
            + WireMessageFieldSizeOf.State /* State (byte) */
            + WireMessageFieldSizeOf.TimeoutMSec /* Timeout (int) */
            + WireMessageFieldSizeOf.Id /* Id 5*(int) */;
    }
}
