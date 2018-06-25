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
    internal static class TimeoutHandler<T>
    {
        private static readonly ConcurrentDictionary<TaskCompletor<T>, RegisteredWaitHandle> _timeoutRegisterations = 
            new ConcurrentDictionary<TaskCompletor<T>, RegisteredWaitHandle>();

        private static void Callback(object state, bool timedOut)
        {
            var taskCompletor = (TaskCompletor<T>)state;
            try
            {
                if (timedOut)
                    taskCompletor.DoTimedOut();
            }
            finally
            {
                _timeoutRegisterations.TryRemove(taskCompletor, out RegisteredWaitHandle waitHandle);
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
                    _timeoutRegisterations[taskCompletor] = 
                        ThreadPool.RegisterWaitForSingleObject(waitHandle, Callback, taskCompletor, timeoutMSec, true);
                    return true;
                }
            }
            return false;
        }

        public static void Unregister(TaskCompletor<T> taskCompletor)
        {
            if (taskCompletor != null &&
                _timeoutRegisterations.TryRemove(taskCompletor, out RegisteredWaitHandle waitHandle))
                waitHandle.Unregister(taskCompletor.WaitHandle);
        }
    }
}
