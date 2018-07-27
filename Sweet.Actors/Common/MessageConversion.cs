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
    public static class MessageConversion
    {
        #region Wire Messages

        public static RemoteMessage ToRemoteMessage(this WireMessage message)
        {
            IMessage msg = null;
            var messageId = WireMessageId.Empty;

            if (message != null)
            {
                switch (message.MessageType)
                {
                    case MessageType.Default:
                        msg = new Message(message.Data, Aid.Parse(message.From), message.Header, message.TimeoutMSec);
                        break;
                    case MessageType.FutureMessage:
                        msg = new FutureMessage(message.Data, Aid.Parse(message.From), message.Header, message.TimeoutMSec);
                        break;
                    case MessageType.FutureResponse:
                        msg = new FutureResponse(message.Data, Aid.Parse(message.From), message.Header);
                        break;
                    case MessageType.FutureError:
                        msg = new FutureError(message.Exception, Aid.Parse(message.From), message.Header);
                        break;
                }

                WireMessageId.TryParse(message.Id, out messageId);
            }

            return new RemoteMessage(msg ?? Message.Empty,
                Aid.Parse(message?.To) ?? Aid.Unknown,
                messageId ?? WireMessageId.Empty);
        }

        public static WireMessage ToWireMessage(this RemoteMessage message, Exception exception = null)
        {
            if (message != null)
                return ToWireMessage(message.Message, message.To, message.MessageId, exception);
            return null;
        }

        public static WireMessage ToWireMessage(this Exception exception, Aid from, Aid to, WireMessageId id = null)
        {
            var faulted = (exception != null);

            var state = WireMessageState.Empty;
            state |= faulted ? WireMessageState.Faulted : WireMessageState.Canceled;

            return new WireMessage
            {
                From = from?.ToString(),
                To = to?.ToString(),
                Exception = exception,
                MessageType = faulted ? MessageType.FutureError : MessageType.FutureResponse,
                Id = id?.ToString() ?? WireMessageId.NextAsString(),
                State = state
            };
        }

        public static WireMessage ToWireMessage(this IMessage message, Aid to, WireMessageId id = null, Exception exception = null)
        {
            var result = new WireMessage { To = to?.ToString(), Id = id?.ToString() ?? WireMessageId.NextAsString() };
            if (message != null)
            {
                result.MessageType = message.MessageType;
                result.From = message.From?.ToString();
                result.Data = message.Data;
                result.TimeoutMSec = message.TimeoutMSec;

                var msgHeader = message.Header;
                var headerCount = msgHeader?.Count ?? 0;

                if (headerCount > 0)
                {
                    var header = new Dictionary<string, string>(headerCount);
                    foreach (var kv in msgHeader)
                    {
                        header.Add(kv.Key, kv.Value);
                    }
                    result.Header = header;
                }

                var state = WireMessageState.Default;
                if (message.IsEmpty)
                    state |= WireMessageState.Empty;

                if (message is IFutureMessage future)
                {
                    if (future.IsCanceled)
                        state |= WireMessageState.Canceled;

                    if (future.IsCompleted)
                        state |= WireMessageState.Completed;

                    if (future.IsFaulted)
                        state |= WireMessageState.Faulted;
                }

                if (exception != null)
                {
                    result.Exception = exception;
                    state |= WireMessageState.Faulted;
                }
                else if (message is IFutureError error)
                {
                    result.Exception = error.Exception;
                    state |= WireMessageState.Faulted;
                }

                result.State = state;
            }
            return result;
        }

        #endregion Wire Messages
    }
}
