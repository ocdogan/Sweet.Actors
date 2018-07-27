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
using System.Threading;

namespace Sweet.Actors
{
    public class RemoteRequest : RemoteMessage
    {
        private TaskCompletor<IFutureResponse> _taskCompletor;

        internal event TimeoutEventHandler OnTimeout;

        internal RemoteRequest(IFutureMessage message, Aid to)
            : base(message, to, WireMessageId.Next())
        {
            _taskCompletor = message.Completor;
            if (message.TimeoutMSec.HasValue)
                _taskCompletor.OnTimeout += DoTimedOut;
        }

        public TaskCompletor<IFutureResponse> Completor => _taskCompletor;

        public override void Dispose()
        {
            var taskCompletor = Interlocked.Exchange(ref _taskCompletor, null);
            if (taskCompletor != null)
            {
                using (taskCompletor)
                {
                    taskCompletor.OnTimeout -= DoTimedOut;
                }
            }
        }

        protected virtual void DoTimedOut(object sender, TaskCompletionStatus status)
        {
            try
            {
                var taskCompletor = _taskCompletor;
                if (taskCompletor != null)
                    taskCompletor.OnTimeout -= DoTimedOut;

                ((IFutureMessage)Message).Cancel();
            }
            finally
            {
                OnTimeout?.Invoke(this, status);
            }
        }
    }
}
