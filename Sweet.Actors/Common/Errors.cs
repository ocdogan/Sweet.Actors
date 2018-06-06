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
    public static class Errors
    {
        public const string StreamIsClosed = "Stream is closed";

        public const string MessageExpired = "Message expired";
        public const string MaxAllowedDataSizeExceeded = "Max allowed data size exceeded";
        public const string InvalidPort = "Invalid port";
        public const string InvalidAddress = "Invalid address";
        public const string InvalidMessageType = "Invalid message type";
        public const string InvalidResponseType = "Invalid response type";
        public const string ActorAlreadyExsists = "Actor with name {0} already exists";
        public const string ActorSystemAlreadyExsists = "Actor system with name {0} already exists";

        public const string ExpectingReceiveCompletedOperation = "Expecting receive completed operation";
    }
}
