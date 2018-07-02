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
    public static class RpcErrors
    {
        public const string RequestCanceled = "request canceled";
        public const string InvalidMessage = "Invalid message";
        public const string InvalidMessageResponse = "Invalid message response";
        public const string InvalidMessageReceiver = "Invalid message receiver";
        public const string InvalidSerializerKey = "Invalid serializer key";
        public const string CannotConnectToRemoteEndPoint = "Can not connect to remote end-point";
        public const string CannotResolveEndPoint = "Can not resolve end-point";
        public const string CannotStartToReceive = "Can not start to receive";
        public const string AnotherActorSystemAlreadyBindedWithName = "Another actor system is already binded with the same name";
    }
}