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
using System.Linq;
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
                _action = action;
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
                var state = Interlocked.Exchange(ref _state, null);
                if (disposing && !(state is null))
                    TimeoutHandler.Unregister(state);
            }

            public void Invoke(bool @async)
            {
                if (!Disposed)
                {
                    try
                    {
                        if (@async)
                            _action.InvokeAsync();
                        else _action?.Invoke();
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
            private readonly Dictionary<object, TimeoutEvent> _registerations = new Dictionary<object, TimeoutEvent>();

            public int Count => _count;

            public bool TryRemove(object state, out TimeoutEvent registry)
            {
                registry = null;
                if (state != null)
                {
                    _syncRoot.EnterUpgradeableReadLock();
                    try
                    {
                        if (_registerations.TryGetValue(state, out TimeoutEvent tmpRegistry))
                        {
                            _syncRoot.EnterWriteLock();
                            try
                            {
                                if (_registerations.Remove(state))
                                {
                                    Interlocked.Add(ref _count, -1);

                                    registry = tmpRegistry;

                                    var prev = registry.Prev;
                                    var next = registry.Next;

                                    if (registry == _head)
                                    {
                                        _head = next;
                                        if (registry == _tail)
                                            _tail = prev;

                                        if (next != null)
                                            next.Prev = null;
                                    }
                                    else if (registry == _tail)
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

                                    registry.Prev = null;
                                    registry.Next = null;

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

            public bool Add(object state, Action action, int timeoutMSec)
            {
                if ((state != null) && (timeoutMSec > 0) && (action != null))
                {
                    _syncRoot.EnterUpgradeableReadLock();
                    try
                    {
                        if (_registerations.TryGetValue(state, out TimeoutEvent registry))
                        {
                            registry.Reset(action, timeoutMSec);
                            return true;
                        }

                        _syncRoot.EnterWriteLock();
                        try
                        {
                            registry = new TimeoutEvent(state, action, timeoutMSec);
                            if (_head == null)
                            {
                                _head = registry;
                                _tail = registry;
                            }
                            else
                            {
                                _tail.Next = registry;
                                registry.Prev = _tail;

                                _tail = registry;
                            }

                            _registerations[state] = registry;
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

            public IEnumerator<TimeoutEvent> GetEnumerator()
            {
                return new Enumerator(this);
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return new Enumerator(this);
            }
        }

        private struct TimedOutPair
        {
            public object State;
            public TimeoutEvent Registry;
        }

        private const int _1K = 1000;
        private const int _10K = 10 * _1K;
        private const int WaitDuration = 1000;

        private static Timer _timer;
        private static int _scheduled;
        private static long _inProcess;
        private static int _circularCount;

        private static readonly TimeoutRegistry _registerations = new TimeoutRegistry();

        private static void Callback(object state, bool @async)
        {
            if (!(state is null) &&
                _registerations.TryRemove(state, out TimeoutEvent registry))
            {
                using (registry)
                    registry.Invoke(@async);
            }
        }

        public static bool TryRegister(object state, Action action, int timeoutMSec)
        {
            if (_registerations.Add(state, action, timeoutMSec))
            {
                Schedule();
                return true;
            }
            return false;
        }

        public static bool Unregister(object state)
        {
            if (!(state is null))
                return _registerations.TryRemove(state, out TimeoutEvent registry);
            return false;
        }

        private static void Schedule()
        {
            if (Interlocked.CompareExchange(ref _scheduled, 1, 0) == 0)
                _timer = new Timer(CheckTimeouts, null, 0, _1K);
        }

        private static void CheckTimeouts(object state)
        {
            Cycle(false);
        }

        private static void Cycle(bool circular)
        {
            if (Interlocked.Add(ref _inProcess, 1) != 1L)
                return;

            try
            {
                if (_registerations.Count == 0)
                    return;

                var headState = 0;
                var head = new TimedOutPair();

                var timedOuts = (List<TimedOutPair>)null;

                var loop = 0;
                var now = Environment.TickCount;

                foreach (var registry in _registerations)
                {
                    if (++loop % _10K == 0)
                        Thread.Sleep(1);

                    if (now >= registry.TimeoutAt)
                    {
                        var pair = new TimedOutPair { State = registry.State, Registry = registry };
                        switch (headState)
                        {
                            case 0:
                                head = pair;
                                headState++;
                                break;
                            case 1:
                                timedOuts = new List<TimedOutPair>();

                                timedOuts.Add(head);
                                timedOuts.Add(pair);

                                headState++;
                                break;
                            default:
                                timedOuts.Add(pair);
                                break;
                        }
                    }
                }

                switch (headState)
                {
                    case 1:
                        {
                            if (_registerations.TryRemove(head.State, out TimeoutEvent registry))
                                registry.Invoke(false);
                        }
                        break;
                    case 2:
                        var count = timedOuts.Count;
                        if (count <= _1K)
                        {
                            for (var i = 0; i < count; i++)
                            {
                                var pair = timedOuts[i];
                                if (_registerations.TryRemove(pair.State, out TimeoutEvent registry))
                                {
                                    registry.Invoke(false);
                                    if (i % 100 == 0)
                                        Thread.Sleep(1);
                                }
                            }
                            return;
                        }

                        Parallel.ForEach(timedOuts,
                            (pair) =>
                            {
                                if (_registerations.TryRemove(pair.State, out TimeoutEvent registry))
                                    registry.Invoke(false);
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
                    Thread.Sleep(10);
                    Cycle(true);
                }
            }
        }
    }
}
