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
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Sweet.Actors
{
    public class Processor<T> : Disposable
    {
        protected const int MinWaitDuration = 50;
        protected const int MaxWaitDuration = 10000;
        protected const int DefaultWaitDuration = 1000;

        protected static readonly Task Completed = Task.FromResult(0);

        private long _inProcess;
        private long _inWaitForSchedule;
        private long _rescheduleRequest;
        private readonly ManualResetEventSlim _resetEvent = new ManualResetEventSlim(false);

        private readonly ConcurrentQueue<T> _queue = new ConcurrentQueue<T>();

        private int _requestWaitDuration = DefaultWaitDuration;
        private int _sequentialInvokeLimit = Constants.DefaultSequentialInvokeLimit;

        public Processor(int sequentialInvokeLimit = -1, int requestWaitDuration = -1)
        {
            SetSequentialInvokeLimit(sequentialInvokeLimit);

            if (requestWaitDuration < 1)
                _requestWaitDuration = DefaultWaitDuration;
            else _requestWaitDuration = Math.Min(MaxWaitDuration, Math.Max(MinWaitDuration, requestWaitDuration));
        }

        protected void SetSequentialInvokeLimit(int sequentialInvokeLimit)
        {
            sequentialInvokeLimit = Common.CheckSequentialInvokeLimit(sequentialInvokeLimit);
            _sequentialInvokeLimit = (sequentialInvokeLimit < 1) ?
                    Constants.DefaultSequentialInvokeLimit : sequentialInvokeLimit;
        }

        protected override void OnDispose(bool disposing)
        {
            Interlocked.Exchange(ref _inProcess, 0L);
            if (disposing)
                _resetEvent.Reset();
        }

        protected bool IsEmpty()
        {
            return _queue.IsEmpty;
        }

        protected bool Processing()
        {
            return Interlocked.Read(ref _inProcess) != 0;
        }

        protected Task Enqueue(T item)
        {
            try
            {
                _queue.Enqueue(item);
                Schedule();

                return Completed;
            }
            catch (Exception e)
            {
                return Task.FromException(e);
            }
        }

        protected void Schedule()
        {
            if (!Disposed)
            {
                if (Interlocked.CompareExchange(ref _inProcess, 1L, 0L) == 0L)
                    Task.Factory.StartNew(ProcessQueue);
                else
                {
                    Interlocked.Exchange(ref _rescheduleRequest, 1L);
                    _resetEvent.Set();
                }
            }
        }

        protected bool WaitForScheduleRequest()
        {
            if (Interlocked.Read(ref _inProcess) != 0L)
            {
                if (Interlocked.CompareExchange(ref _inWaitForSchedule, 1L, 0L) != 0L)
                {
                    _resetEvent.Set();
                    return Interlocked.Read(ref _rescheduleRequest) != 0;
                }

                var requested = false;
                try
                {
                    requested = IsScheduleRequested() ||
                        _resetEvent.Wait(_requestWaitDuration, new CancellationToken());
                }
                finally
                {
                    requested = requested || Interlocked.Read(ref _rescheduleRequest) != 0L;

                    Interlocked.Exchange(ref _inWaitForSchedule, 0L);
                    ResetScheduleRequest();
                }
                return requested;
            }
            return false;
        }

        protected bool IsScheduleRequested()
        {
            return Interlocked.Read(ref _rescheduleRequest) != 0L ||
                Interlocked.Read(ref _inWaitForSchedule) != 0L ||
                _resetEvent.IsSet;
        }

        protected void ResetScheduleRequest()
        {
            Interlocked.Exchange(ref _rescheduleRequest, 0L);
            if (Interlocked.Read(ref _inWaitForSchedule) != 0L)
                _resetEvent.Set();
            else _resetEvent.Reset();
        }

        private Task ProcessQueue()
        {
            if (!Disposed)
            {
                try
                {
                    do
                    {
                        ResetScheduleRequest();

                        var onBefore = InitProcessCycle(out bool @continue);
                        if (onBefore.IsFaulted || onBefore.IsCanceled)
                            return onBefore;

                        if (!@continue)
                            break;

                        var exception = (Exception)null;
                        try
                        {
                            ProcessItems();
                        }
                        catch (Exception e)
                        {
                            exception = e;
                            throw;
                        }
                        finally
                        {
                            CompleteProcessCycle(exception, out @continue);
                        }

                        if (!@continue)
                            break;
                    } while (WaitForScheduleRequest());
                }
                finally
                {
                    if (!Disposed)
                    {
                        ResetScheduleRequest();
                        Interlocked.Exchange(ref _inProcess, 0L);

                        if (!_queue.IsEmpty)
                            Schedule();
                    }
                }
            }
            return Completed;
        }

        protected virtual Task InitProcessCycle(out bool @continue)
        { 
            @continue = true;
            return Completed;
        }

        protected virtual void CompleteProcessCycle(Exception exception, out bool @continue)
        { 
            @continue = true;
        }

        private void ProcessItems()
        {
            for (var i = 0; i < _sequentialInvokeLimit; i++)
            {
                if ((Interlocked.Read(ref _inProcess) != 1L) ||
                    !_queue.TryDequeue(out T item))
                    break;

                var task = ProcessItem(item, CanFlush());
                if (task.IsFaulted || task.IsCanceled)
                    continue;

                if (!task.IsCompleted)
                    task.ContinueWith((previousTask) =>
                    {
                        if (!(Disposed || _queue.IsEmpty))
                            Schedule();
                    });
            }
        }

        protected virtual bool CanFlush()
        {
            return true;
        }

        protected virtual Task ProcessItem(T item, bool flush)
        {
            return Completed;
        }
    }
}
