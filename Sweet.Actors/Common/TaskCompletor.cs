﻿#region License
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
    public class TaskCompletor<T> : Disposable
    {
        private int _timeoutMSec;
        private int _unregistered;
        private CancellationTokenSource _cts;
        private TaskCompletionSource<T> _tcs;

        internal event EventHandler OnTimeout;

        internal TaskCompletor(int timeoutMSec = -1)
        {
            _cts = new CancellationTokenSource();
            _tcs = new TaskCompletionSource<T>(_cts);

            if (timeoutMSec < 0)
                _timeoutMSec = Constants.MaxRequestTimeoutMSec;
            else if (timeoutMSec == 0)
                _timeoutMSec = Constants.DefaultRequestTimeoutMSec;
            else 
                _timeoutMSec = Math.Min(Constants.MaxRequestTimeoutMSec, timeoutMSec);

            TimeoutHandler<T>.TryRegister(this, _timeoutMSec);
        }

        protected override void OnDispose(bool disposing)
        {
            if (disposing)
                Unregister();
            base.OnDispose(disposing);
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

            _cts.Cancel();
            return _tcs.TrySetCanceled(_cts.Token);
        }

        public bool TrySetException(IEnumerable<Exception> exceptions)
        {
            Unregister();
            return _tcs.TrySetException(exceptions);
        }

        public bool TrySetException(Exception exception)
        {
            Unregister();
            return _tcs.TrySetException(exception);
        }

        public bool TrySetResult(T result)
        {
            Unregister();
            return _tcs.TrySetResult(result);
        }

        internal virtual void DoTimedOut()
        {
            Interlocked.Exchange(ref _unregistered, 1);

            var status = _tcs.Task.Status;
            if (!(status == TaskStatus.RanToCompletion ||
                status == TaskStatus.Canceled || status == TaskStatus.Faulted))
            {
                TrySetCanceled();
                OnTimeout?.Invoke(this, EventArgs.Empty);
            }
        }

        public void Unregister()
        {
            if (Interlocked.CompareExchange(ref _unregistered, 1, 0) == 0)
                TimeoutHandler<T>.Unregister(this);
        }
    }
}
