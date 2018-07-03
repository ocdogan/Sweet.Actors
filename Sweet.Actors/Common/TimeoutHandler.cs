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
    internal static class TimeoutHandler
    {
        private class TimeoutRegistry : Disposable
        {
            private int _hashCode;
            private object _state;
            private Action _action;
            private int _timeoutAt;

            public TimeoutRegistry(object state, Action action, int timeoutMSec)
            {
                _state = state;
                _action = action;
                _timeoutAt = timeoutMSec < 1 ? int.MaxValue : Environment.TickCount + timeoutMSec;
            }

            public object State => _state;

            public bool Expired => Environment.TickCount >= _timeoutAt;

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

                if (obj is TimeoutRegistry ts)
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

        private struct TimedOutPair
        {
            public object State;
            public TimeoutRegistry Registry;
        }

        private const int WaitDuration = 1000;

        private static Timer _timer;
        private static int _scheduled;
        private static long _inProcess;

        private static readonly ConcurrentDictionary<object, TimeoutRegistry> _registerations = 
            new ConcurrentDictionary<object, TimeoutRegistry>();

        private static void Callback(object state, bool @async)
        {
            if (!(state is null) &&
                _registerations.TryRemove(state, out TimeoutRegistry registry))
            {
                using (registry)
                    registry.Invoke(@async);
            }
        }

        public static bool TryRegister(object state, Action action, int timeoutMSec)
        {
            if (!(state is null) && (timeoutMSec > 0) && (action != null))
            {
                _registerations.TryRemove(state, out TimeoutRegistry registry);

                _registerations[state] = new TimeoutRegistry(state, action, timeoutMSec);
                Schedule();

                return true;
            }
            return false;
        }

        public static bool Unregister(object state)
        {
            if (!(state is null))
                return _registerations.TryRemove(state, out TimeoutRegistry registry);
            return false;
        }

        private static void Schedule()
        {
            if (Interlocked.CompareExchange(ref _scheduled, 1, 0) == 0)
                _timer = new Timer(CheckTimeouts, null, 0L, 20L);
        }

        private static void CheckTimeouts(object state)
        {
            if (Interlocked.Increment(ref _inProcess) == 1L)
            {
                try
                {
                    var headState = 0;
                    var head = new TimedOutPair { State = null, Registry = null };

                    var timedOuts = (PartitionedList<TimedOutPair>)null;

                    foreach (var kv in _registerations)
                    {
                        if (kv.Value.Expired)
                        {
                            var pair = new TimedOutPair { State = kv.Key, Registry = kv.Value };

                            if (headState == 0)
                            {
                                headState = 1;
                                head = pair;
                            }
                            else
                            {
                                if (timedOuts == null)
                                    timedOuts = new PartitionedList<TimedOutPair>();

                                if (headState == 1)
                                {
                                    timedOuts.Put(pair);
                                    headState = 2;
                                }

                                timedOuts.Put(pair);
                            }
                        }
                    }

                    var loopCount = 0;
                    if (headState == 1)
                    {
                        var @async = (loopCount++ >= 100);

                        if (_registerations.TryRemove(head.State, out TimeoutRegistry registry))
                            registry.Invoke(async);

                        if (@async)
                        {
                            loopCount = 0;
                            Thread.Sleep(1);
                        }
                    }
                    else if (headState == 2 && timedOuts != null)
                    {
                        try
                        {
                            foreach (var pair in timedOuts)
                            {
                                var @async = (loopCount++ >= 100);

                                if (_registerations.TryRemove(pair.State, out TimeoutRegistry registry))
                                    registry.Invoke(@async);

                                if (@async)
                                {
                                    loopCount = 0;
                                    Thread.Sleep(1);
                                }
                            }
                        }
                        finally
                        {
                            timedOuts.Dispose();
                        }
                    }
                }
                catch (Exception)
                { }
                finally
                {
                    if (Interlocked.Exchange(ref _inProcess, 0L) > 1L)
                    {
                        Thread.Sleep(1);
                        if (Interlocked.Read(ref _inProcess) == 0L)
                            CheckTimeouts(null);
                    }
                }
            }
        }
    }
}
