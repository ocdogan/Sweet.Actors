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
        public static (IMessage, Address) RpcMessageToActual(this RpcMessage rpcMsg)
        {
            IMessage msg = null;
            if (rpcMsg != null)
            {
                switch (rpcMsg.MessageType)
                {
                    case MessageType.Default:
                        msg = new Message(rpcMsg.Data, rpcMsg.From, rpcMsg.Header);
                        break;
                    case MessageType.FutureMessage:
                        msg = MessageFactory.CreateFutureMessage(Type.GetType(rpcMsg.ResponseType), rpcMsg.Data,
                            rpcMsg.From, rpcMsg.Header, rpcMsg.TimeoutMSec);
                        break;
                    case MessageType.FutureResponse:
                        msg = MessageFactory.CreateFutureResponse(Type.GetType(rpcMsg.ResponseType), rpcMsg.Data,
                            rpcMsg.From, rpcMsg.Header);
                        break;
                    case MessageType.FutureError:
                        msg = MessageFactory.CreateFutureError(Type.GetType(rpcMsg.ResponseType), rpcMsg.Exception,
                            rpcMsg.From, rpcMsg.Header);
                        break;
                }
            }
            return (msg ?? Message.Empty, rpcMsg?.To ?? Address.Unknown);
        }

        public static RpcMessage ActualMessageToRpc(this IMessage msg, Address to)
        {
            var result = new RpcMessage{ To = to };
            if (msg != null)
            {
                result.Data = msg.Data;
                result.MessageType = msg.MessageType;
                result.From = msg.From;

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