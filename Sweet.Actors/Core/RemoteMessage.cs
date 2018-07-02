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

namespace Sweet.Actors
{
    public class RemoteMessage : IDisposable
    {
        private Aid _to;
        private bool _isFuture;
        private IMessage _message;
        private WireMessageId _messageId;

        public RemoteMessage(IMessage message, Aid to, WireMessageId messageId)
        {
            _message = message ?? Actors.Message.Empty;
            _to = to ?? Aid.Unknown;
            _messageId = messageId ?? WireMessageId.Next();
            _isFuture = _message is IFutureMessage;
        }

        public IMessage Message => _message;

        public Aid To => _to;

        public WireMessageId MessageId => _messageId;

        public bool IsFuture => _isFuture;

        public virtual void Dispose()
        {
            _message = null;
        }
    }
}
