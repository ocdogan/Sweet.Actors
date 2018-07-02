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

namespace Sweet.Actors
{
    internal class TimeoutRegistery : Disposable
    {
        private int _hashCode;
        private object _state;
        private WaitHandle _waitHandle;
        private RegisteredWaitHandle _registeredWaitHandle;

        internal TimeoutRegistery(WaitHandle waitHandle, 
            RegisteredWaitHandle registeredWaitHandle, object state)
        {
            _state = state;
            _waitHandle = waitHandle;
            _registeredWaitHandle = registeredWaitHandle;
        }

        public object State => _state;
        
        public RegisteredWaitHandle RegisteredWaitHandle => _registeredWaitHandle;
        
        public WaitHandle WaitHandle => _waitHandle;

        public override int GetHashCode()
        {
            if (_hashCode == 0)
            {
                var hash = 1 + (_waitHandle?.GetHashCode() ?? 0);
                _hashCode = 31 * hash + (_registeredWaitHandle?.GetHashCode() ?? 0);
            }
            return _hashCode;
        }

        public override bool Equals(object obj)
        {
            if (obj == null)
                return false;

            if (obj is TimeoutRegistery ts)
                return (ts.GetHashCode() == GetHashCode()) &&
                    (Disposed || ts._waitHandle == _waitHandle);
            return false;
        }

        protected override void OnDispose(bool disposing)
        {
            var waitHandle = Interlocked.Exchange(ref _waitHandle, null);
            var registeredWaitHandle = Interlocked.Exchange(ref _registeredWaitHandle, null);

            if (!disposing)
                _state = null;

            if (waitHandle != null)
            {
                try
                {
                    registeredWaitHandle?.Unregister(waitHandle);
                }
                catch (Exception)
                { }
            }
        }
    }

    internal static class TimeoutHandler
    {
        private static readonly ConcurrentDictionary<WaitHandle, TimeoutRegistery> _timeoutRegisterations = 
            new ConcurrentDictionary<WaitHandle, TimeoutRegistery>();

        private static void CallbackWaitHandle(object state, bool timedOut)
        {
            if (state is WaitHandle waitHandle &&
                _timeoutRegisterations.TryRemove(waitHandle, out TimeoutRegistery registery))
            {
                using (registery)
                {
                    try
                    {
                        if (timedOut && registery.State is Action action)
                            action?.Invoke();
                    }
                    catch (Exception)
                    { }
                }
            }
        }

        public static bool TryRegister(WaitHandle waitHandle, int timeoutMSec, Action action)
        {
            if ((waitHandle != null) && (timeoutMSec > 0) && (action != null))
            {
                Unregister(waitHandle);

                if (!(waitHandle.SafeWaitHandle?.IsClosed ?? true))
                {                     
                    _timeoutRegisterations[waitHandle] = 
                        new TimeoutRegistery(waitHandle, 
                            ThreadPool.RegisterWaitForSingleObject(waitHandle, CallbackWaitHandle, waitHandle, timeoutMSec, true),
                            action);
                    return true;
                }
            }
            return false;
        }

        private static void Unregister(TimeoutRegistery registery)
        {
            try
            {
                registery?.RegisteredWaitHandle?.Unregister(registery?.WaitHandle);
            }
            catch (Exception)
            { }
        }

        public static void Unregister(WaitHandle waitHandle)
        {
            if (waitHandle != null &&
                _timeoutRegisterations.TryRemove(waitHandle, out TimeoutRegistery registery))
                Unregister(registery);
        }
    }

    internal static class TimeoutHandler<T>
    {
        private static readonly ConcurrentDictionary<WaitHandle, RegisteredWaitHandle> _timeoutRegisterations = 
            new ConcurrentDictionary<WaitHandle, RegisteredWaitHandle>();

        private static void CallbackTaskCompletor(object state, bool timedOut)
        {
            if (state is TaskCompletor<T> taskCompletor)
            {
                var waitHandle = taskCompletor.WaitHandle;
                try
                {
                    if (timedOut)
                        taskCompletor.DoTimedOut();
                }
                finally
                {
                    if (_timeoutRegisterations.TryRemove(waitHandle, out RegisteredWaitHandle registeredWaitHandle))
                        Unregister(waitHandle, registeredWaitHandle);
                }
            }
        }

        public static bool TryRegister(TaskCompletor<T> taskCompletor, int timeoutMSec)
        {
            if ((taskCompletor != null) && (timeoutMSec > 0) && !taskCompletor.IsCanceled)
            {
                Unregister(taskCompletor);

                var waitHandle = taskCompletor.WaitHandle;
                if (!(waitHandle.SafeWaitHandle?.IsClosed ?? true))
                {
                    _timeoutRegisterations[waitHandle] =
                        ThreadPool.RegisterWaitForSingleObject(waitHandle, CallbackTaskCompletor, taskCompletor, timeoutMSec, true);
                    return true;
                }
            }
            return false;
        }

        private static void Unregister(WaitHandle waitHandle, RegisteredWaitHandle registeredWaitHandle)
        {
            try
            {
                if (waitHandle != null)
                    registeredWaitHandle?.Unregister(waitHandle);
            }
            catch (Exception)
            { }
        }

        public static void Unregister(TaskCompletor<T> taskCompletor)
        {
            if (taskCompletor != null)
            {
                var waitHandle = taskCompletor.WaitHandle;
                if (_timeoutRegisterations.TryRemove(waitHandle, out RegisteredWaitHandle registeredWaitHandle))
                    Unregister(waitHandle, registeredWaitHandle);
            }
        }
    }
}
