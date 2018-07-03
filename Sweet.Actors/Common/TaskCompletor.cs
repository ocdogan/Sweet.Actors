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
using System.Threading;
using System.Threading.Tasks;

namespace Sweet.Actors
{
    internal delegate void TimeoutEventHandler(object sender, TaskCompletionStatus status);

    public class TaskCompletor<T> : Disposable
    {
        private int _timeoutMSec;
        private int _unregistered;
        private CancellationTokenSource _cts;
        private TaskCompletionSource<T> _tcs;

        internal event TimeoutEventHandler OnTimeout;

        internal TaskCompletor(int timeoutMSec = 0)
        {
            _cts = new CancellationTokenSource();
            _tcs = new TaskCompletionSource<T>(_cts);

            _timeoutMSec = Common.CheckMessageTimeout(timeoutMSec);

            // TimeoutHandler<T>.TryRegister(this, _timeoutMSec);
        }

        protected override void OnDispose(bool disposing)
        {
            TrySetCanceled();
        }

        public virtual bool IsCanceled => _tcs.Task.IsCanceled ||
            ((_cts != null) && _cts.IsCancellationRequested);

        public virtual bool IsCompleted => _tcs.Task.IsCompleted;

        public virtual bool IsFaulted => _tcs.Task.IsFaulted;

        public int TimeoutMSec => _timeoutMSec;

        public Task<T> Task => _tcs.Task;

        public CancellationToken Token => _cts.Token;

        public WaitHandle WaitHandle => _cts.Token.WaitHandle;

        public bool TrySetCanceled()
        {
            Unregister();

            var task = _tcs?.Task;
            if (task != null && !task.IsCompleted)
            {
                _cts?.Cancel();
                return _tcs?.TrySetCanceled(_cts.Token) ?? false;
            }
            return false;
        }

        public bool TrySetException(IEnumerable<Exception> exceptions)
        {
            Unregister();
            return _tcs?.TrySetException(exceptions) ?? false;
        }

        public bool TrySetException(Exception exception)
        {
            Unregister();
            return _tcs?.TrySetException(exception) ?? false;
        }

        public bool TrySetResult(T result)
        {
            Unregister();
            return _tcs?.TrySetResult(result) ?? false;
        }

        internal virtual void DoTimedOut()
        {
            var status = TaskStatus.RanToCompletion;
            try
            {
                Interlocked.Exchange(ref _unregistered, 1);

                status = _tcs?.Task?.Status ?? TaskStatus.RanToCompletion;
                if (!(status == TaskStatus.RanToCompletion ||
                    status == TaskStatus.Canceled || status == TaskStatus.Faulted))
                {
                    TrySetCanceled();
                    status = _tcs?.Task?.Status ?? TaskStatus.RanToCompletion;
                }
            }
            finally
            {
                OnTimeout?.Invoke(this, ToTaskCompletionStatus(status));
            }
        }

        private static TaskCompletionStatus ToTaskCompletionStatus(TaskStatus tStatus)
        {
            TaskCompletionStatus status;
            switch (tStatus)
            {
                case TaskStatus.Canceled:
                    status = TaskCompletionStatus.Canceled;
                    break;
                case TaskStatus.Created:
                    status = TaskCompletionStatus.Created;
                    break;
                case TaskStatus.Faulted:
                    status = TaskCompletionStatus.Failed;
                    break;
                default:
                    status = TaskCompletionStatus.Running;
                    break;
            }

            return status;
        }

        public void Unregister()
        {
            /* if (Interlocked.CompareExchange(ref _unregistered, 1, 0) == 0)
                TimeoutHandler<T>.Unregister(this); */
        }
    }
}
