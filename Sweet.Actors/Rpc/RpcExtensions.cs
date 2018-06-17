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

namespace Sweet.Actors
{
    public static class RpcExtensions
    {
        public static (IMessage, Aid) ToActualMessage(this RpcMessage rpcMsg)
        {
            IMessage message = null;
            if (rpcMsg != null)
            {
                switch (rpcMsg.MessageType)
                {
                    case MessageType.Default:
                        message = new Message(rpcMsg.Data, Aid.Parse(rpcMsg.From), rpcMsg.Header);
                        break;
                    case MessageType.FutureMessage:
                        message = MessageFactory.CreateFutureMessage(Type.GetType(rpcMsg.ResponseType), rpcMsg.Data,
                            Aid.Parse(rpcMsg.From), rpcMsg.Header, rpcMsg.TimeoutMSec);
                        break;
                    case MessageType.FutureResponse:
                        message = MessageFactory.CreateFutureResponse(Type.GetType(rpcMsg.ResponseType), rpcMsg.Data,
                            Aid.Parse(rpcMsg.From), rpcMsg.Header);
                        break;
                    case MessageType.FutureError:
                        message = MessageFactory.CreateFutureError(Type.GetType(rpcMsg.ResponseType), rpcMsg.Exception,
                            Aid.Parse(rpcMsg.From), rpcMsg.Header);
                        break;
                }
            }
            return (message ?? Message.Empty, Aid.Parse(rpcMsg?.To) ?? Aid.Unknown);
        }

        public static RpcMessage ToRpcMessage(this IMessage msg, Aid to, RpcMessageId id = null)
        {
            var result = new RpcMessage{ To = to?.ToString(), Id = id?.ToString() ?? RpcMessageId.NextAsString() };
            if (msg != null)
            {
                result.MessageType = msg.MessageType;
                result.From = msg.From?.ToString();
                result.Data = msg.Data;

                var msgHeader = msg.Header;
                if (msgHeader != null)
                {
                    var header = new Dictionary<string, string>(msgHeader.Count);
                    foreach (var kv in msgHeader)
                    {
                        header.Add(kv.Key, kv.Value);
                    }
                    result.Header = header;
                }

                var state = RpcMessageState.Default;
                if (msg is IFutureMessage future)
                {
                    result.TimeoutMSec = future.TimeoutMSec;
                    result.ResponseType = future.ResponseType?.ToString();

                    if (future.IsCanceled)
                        state |= RpcMessageState.Canceled;

                    if (future.IsCompleted)
                        state |= RpcMessageState.Completed;
                        
                    if (future.IsFaulted)
                        state |= RpcMessageState.Faulted;
                }

                if (msg is IFutureError error)
                {
                    result.Exception = error.Exception;

                    if (error.IsFaulted)
                        state |= RpcMessageState.Faulted;                        
                }

                if (msg is IFutureResponse resp)
                {
                    if (resp.IsEmpty)
                        state |= RpcMessageState.Empty;
                }

                result.State = state;
            }
            return result;
        }
    }
}