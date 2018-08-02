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
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Sweet.Actors
{
    internal static class TimeoutHandler
    {
        private class TimeoutEvent : Disposable
        {
            private int _hashCode;
            private object _state;
            private Action _action;
            private int _timeoutAt;

            public TimeoutEvent Prev;
            public TimeoutEvent Next;

            public TimeoutEvent(object state, Action action, int timeoutMSec)
            {
                _state = state;
                _action = action;
                _timeoutAt = timeoutMSec < 1 ? int.MaxValue : Environment.TickCount + timeoutMSec;
            }

            public Action Action => _action;

            public object State => _state;

            public int TimeoutAt => _timeoutAt;

            public void Reset(Action action, int timeoutMSec)
            {
                Interlocked.Exchange(ref _action, action);
                _timeoutAt = timeoutMSec < 1 ? int.MaxValue : Environment.TickCount + timeoutMSec;
            }

            public override int GetHashCode()
            {
                if (_hashCode == 0)
                    return (_hashCode = 1 + (_state?.GetHashCode() ?? 0));
                return _hashCode;
            }

            public override bool Equals(object obj)
            {
                if (obj == null)
                    return false;

                if (obj is TimeoutEvent ts)
                    return (ts.GetHashCode() == GetHashCode()) &&
                        ts._state == _state;
                return false;
            }

            protected override void OnDispose(bool disposing)
            {
                var state = Clear();
                if (disposing && !(state is null))
                    TimeoutHandler.Unregister(state);
            }

            public object Clear()
            {
                Prev = null;
                Next = null;
                Interlocked.Exchange(ref _action, null);
                return Interlocked.Exchange(ref _state, null);
            }

            public void Invoke(bool @async)
            {
                if (!Disposed)
                {
                    try
                    {
                        var action = _action;
                        if (action != null)
                        {
                            if (@async)
                                action.InvokeAsync();
                            else action?.Invoke();
                        }
                    }
                    catch (Exception)
                    { }
                }
            }
        }

        private class TimeoutRegistry : IEnumerable<TimeoutEvent>, IEnumerable
        {
            private struct Enumerator : IEnumerator<TimeoutEvent>, IEnumerator
            {
                private TimeoutEvent _next;
                private TimeoutEvent _current;
                private TimeoutRegistry _list;

                public Enumerator(TimeoutRegistry list)
                {
                    _list = list;
                    _current = null;
                    _next = list._head;
                }

                public TimeoutEvent Current => _current;

                object IEnumerator.Current => _current;

                public bool MoveNext()
                {
                    if (_next == null)
                        return false;

                    _list._syncRoot.EnterUpgradeableReadLock();
                    try
                    {
                        _current = _next;
                        _next = _next.Next;
                        if (_next == _list._head)
                            _next = null;
                    }
                    finally
                    {
                        _list._syncRoot.ExitUpgradeableReadLock();
                    }

                    return true;
                }

                public void Reset()
                {
                    _current = null;
                    _list._syncRoot.EnterUpgradeableReadLock();
                    try
                    {
                        _next = _list._head;
                    }
                    finally
                    {
                        _list._syncRoot.ExitUpgradeableReadLock();
                    }
                }

                public void Dispose()
                { }
            }

            private int _count;
            private TimeoutEvent _head;
            private TimeoutEvent _tail;

            private ReaderWriterLockSlim _syncRoot = new ReaderWriterLockSlim();
            private readonly Dictionary<object, TimeoutEvent> _events = new Dictionary<object, TimeoutEvent>();

            public int Count => _count;

            public bool Add(object state, Action action, int timeoutMSec)
            {
                if ((state != null) && (timeoutMSec > 0) && (action != null))
                {
                    _syncRoot.EnterUpgradeableReadLock();
                    try
                    {
                        if (_events.TryGetValue(state, out TimeoutEvent @event))
                        {
                            @event.Reset(action, timeoutMSec);
                            return true;
                        }

                        _syncRoot.EnterWriteLock();
                        try
                        {
                            @event = new TimeoutEvent(state, action, timeoutMSec);
                            if (_head == null)
                            {
                                _head = @event;
                                _tail = @event;
                            }
                            else
                            {
                                _tail.Next = @event;
                                @event.Prev = _tail;

                                _tail = @event;
                            }

                            _events[state] = @event;
                            Interlocked.Add(ref _count, 1);

                            return true;
                        }
                        finally
                        {
                            _syncRoot.ExitWriteLock();
                        }
                    }
                    finally
                    {
                        _syncRoot.ExitUpgradeableReadLock();
                    }
                }
                return false;
            }

            public bool TryRemove(object state, out TimeoutEvent @event)
            {
                @event = null;
                if (state != null)
                {
                    _syncRoot.EnterUpgradeableReadLock();
                    try
                    {
                        if (_events.TryGetValue(state, out TimeoutEvent tmpEvent))
                        {
                            _syncRoot.EnterWriteLock();
                            try
                            {
                                if (_events.Remove(state))
                                {
                                    Interlocked.Add(ref _count, -1);

                                    @event = tmpEvent;

                                    var prev = @event.Prev;
                                    var next = @event.Next;

                                    if (@event == _head)
                                    {
                                        _head = next;
                                        if (@event == _tail)
                                            _tail = prev;

                                        if (next != null)
                                            next.Prev = null;
                                    }
                                    else if (@event == _tail)
                                    {
                                        _tail = prev;
                                        if (prev != null)
                                            prev.Next = null;
                                    }
                                    else
                                    {
                                        if (prev != null)
                                            prev.Next = next;

                                        if (next != null)
                                            next.Prev = prev;
                                    }

                                    @event.Clear();

                                    return true;
                                }
                            }
                            finally
                            {
                                _syncRoot.ExitWriteLock();
                            }
                        }
                    }
                    finally
                    {
                        _syncRoot.ExitUpgradeableReadLock();
                    }
                }
                return false;
            }

            public IEnumerator<TimeoutEvent> GetEnumerator()
            {
                return new Enumerator(this);
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return new Enumerator(this);
            }
        }

        private const int _1K = 1000;
        private const int _10K = 10 * _1K;

        private static Timer _timer;
        private static int _scheduled;
        private static long _inProcess;

        private static readonly TimeoutRegistry _registry = new TimeoutRegistry();

        private static void Callback(object state, bool @async)
        {
            if (!(state is null) &&
                _registry.TryRemove(state, out TimeoutEvent @event))
            {
                using (@event)
                    @event.Invoke(@async);
            }
        }

        public static bool TryRegister(object state, Action action, int timeoutMSec)
        {
            if (_registry.Add(state, action, timeoutMSec))
            {
                Schedule();
                return true;
            }
            return false;
        }

        public static bool Unregister(object state)
        {
            if (!(state is null))
                return _registry.TryRemove(state, out TimeoutEvent @event);
            return false;
        }

        private static void Schedule()
        {
            if (Interlocked.CompareExchange(ref _scheduled, 1, 0) == 0)
                _timer = new Timer((state) => { Cycle(); }, null, 0, _1K);
        }

        private static void Cycle()
        {
            if (Interlocked.Add(ref _inProcess, 1) != 1L)
                return;

            try
            {
                if (_registry.Count == 0)
                    return;

                var head = (object)null;
                var timedOuts = (List<object>)null;

                var cyclingState = 0;

                var loopIndex = 0;
                var now = Environment.TickCount;

                foreach (var @event in _registry)
                {
                    if (++loopIndex % _1K == 0)
                    {
                        Thread.Sleep(1);
                        now = Environment.TickCount;
                    }

                    if (now >= @event.TimeoutAt)
                    {
                        switch (cyclingState)
                        {
                            case 0:
                                head = @event.State;
                                if (!(head is null))
                                    cyclingState++;
                                break;
                            case 1:
                                {
                                    var state = @event.State;
                                    if (!(state is null))
                                    {
                                        timedOuts = new List<object> { head, state };
                                        cyclingState++;
                                    }
                                }
                                break;
                            default:
                                {
                                    var state = @event.State;
                                    if (!(state is null))
                                        timedOuts.Add(state);
                                }
                                break;
                        }
                    }
                }

                switch (cyclingState)
                {
                    case 1:
                        {
                            if (_registry.TryRemove(head, out TimeoutEvent @event))
                                @event.Invoke(false);
                        }
                        break;
                    case 2:
                        var count = timedOuts.Count;
                        if (count <= _1K)
                        {
                            for (var i = 0; i < count; i++)
                            {
                                if (_registry.TryRemove(timedOuts[i], out TimeoutEvent @event))
                                {
                                    @event.Invoke(false);
                                    if (i % 100 == 0)
                                        Thread.Sleep(1);
                                }
                            }
                            return;
                        }

                        Parallel.ForEach(timedOuts,
                            (pair) =>
                            {
                                if (_registry.TryRemove(pair, out TimeoutEvent @event))
                                    @event.Invoke(false);
                            });
                        break;
                    default:
                        break;
                }
            }
            catch (Exception)
            { }
            finally
            {
                if (Interlocked.Exchange(ref _inProcess, 0L) > 1L)
                {
                    Thread.Sleep(1);
                    if (_registry.Count > 0)
                        Cycle();
                }
            }
        }
    }
}
