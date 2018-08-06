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
using System.Collections.Generic;
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
        protected static readonly Task Canceled = Task.FromCanceled(new CancellationToken(true));

        private long _inProcess;
        private long _scheduleRequested;

        private long _count;
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
        }

        protected int SequentialInvokeLimit => _sequentialInvokeLimit;

        protected bool IsEmpty()
        {
            return _queue.IsEmpty;
        }

        protected int Count()
        {
            return Math.Max(0, (int)Interlocked.Read(ref _count));
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
                Interlocked.Add(ref _count, 1);

                Schedule();

                return Completed;
            }
            catch (Exception e)
            {
                return Task.FromException(e);
            }
        }

        protected Task Enqueue(T[] items)
        {
            var count = items?.Length ?? 0;
            if (count > 0)
            {
                try
                {
                    for (var i = 0; i < count; i++)
                    {
                        _queue.Enqueue(items[i]);
                        Interlocked.Add(ref _count, 1);
                    }

                    Schedule();

                    return Completed;
                }
                catch (Exception e)
                {
                    return Task.FromException(e);
                }
            }
            return Completed;
        }

        protected Task Enqueue(IList<T> items)
        {
            var count = items?.Count ?? 0;
            if (count > 0)
            {
                try
                {
                    for (var i = 0; i < count; i++)
                    {
                        _queue.Enqueue(items[i]);
                        Interlocked.Add(ref _count, 1);
                    }

                    Schedule();

                    return Completed;
                }
                catch (Exception e)
                {
                    return Task.FromException(e);
                }
            }
            return Completed;
        }

        protected Task Enqueue(IEnumerable<T> items)
        {
            if (items != null)
            {
                try
                {
                    var enqueued = false;

                    foreach (var item in items)
                    {
                        _queue.Enqueue(item);

                        Interlocked.Add(ref _count, 1);
                        enqueued = true;
                    }

                    if (enqueued)
                        Schedule();

                    return Completed;
                }
                catch (Exception e)
                {
                    return Task.FromException(e);
                }
            }
            return Completed;
        }

        protected void Schedule()
        {
            if (!Disposed)
            {
                if (Interlocked.CompareExchange(ref _inProcess, 1L, 0L) == 0L)
                    Task.Factory.StartNew(ProcessQueue);
                else
                    Interlocked.Exchange(ref _scheduleRequested, 1L);
            }
        }

        protected bool WaitForScheduleRequest()
        {
            return Processing() && IsScheduleRequested() && !Disposed;
        }

        protected bool IsScheduleRequested()
        {
            return Interlocked.Read(ref _scheduleRequested) != 0L;
        }

        protected void ResetScheduleRequest()
        {
            Interlocked.Exchange(ref _scheduleRequested, 0L);
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

                        try
                        {
                            ProcessItems();

                            CompleteProcessCycle(null, out @continue);
                            if (!@continue)
                                break;
                        }
                        catch (Exception e)
                        {
                            CompleteProcessCycle(e, out @continue);
                            if (!@continue)
                                throw;
                        }
                    } while (WaitForScheduleRequest());
                }
                finally
                {
                    StopProcessing();
                    if (!(Disposed || IsEmpty()))
                        Schedule();
                }
            }
            return Completed;
        }

        protected virtual void StopProcessing()
        {
            ResetScheduleRequest();
            Interlocked.Exchange(ref _inProcess, 0L);
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

        protected virtual bool TryDequeue(out T item)
        {
            if (_queue.TryDequeue(out item))
            {
                Interlocked.Add(ref _count, -1);
                return true;
            }
            return false;
        }

        protected virtual bool TryDequeue(int count, out IList<T> list)
        {
            if (count <= 0)
            {
                list = null;
                return false;
            }

            list = new List<T>(Math.Min(count, 100));
            while (_queue.TryDequeue(out T item))
            {
                Interlocked.Add(ref _count, -1);
                list.Add(item);

                if (--count <= 0)
                    break;
            }
            return list.Count > 0;
        }

        protected virtual void ProcessItems()
        {
            for (var i = 0; i < SequentialInvokeLimit; i++)
            {
                if (!Processing() || !TryDequeue(out T item))
                    break;

                var task = ProcessItem(item, CanFlush());
                if (task.IsFaulted || task.IsCanceled)
                    continue;

                var status = task.Status;
                if (!(status == TaskStatus.RanToCompletion || 
                    status == TaskStatus.Canceled || status == TaskStatus.Faulted))
                    task.ContinueWith((previousTask) =>
                    {
                        if (!(Disposed || IsEmpty()))
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
