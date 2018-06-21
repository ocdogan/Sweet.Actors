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
    internal static class TimeoutHandler
    {
        private static readonly ConcurrentDictionary<CancellationTokenSource, RegisteredWaitHandle> _timeoutRegisterations = 
            new ConcurrentDictionary<CancellationTokenSource, RegisteredWaitHandle>();

        private static void Callback(object state, bool timedOut)
        {
            var cancellation = (CancellationTokenSource)state;
            try
            {
                if (timedOut && !cancellation.IsCancellationRequested)
                    cancellation.Cancel();
            }
            finally
            {
                _timeoutRegisterations.TryRemove(cancellation, out RegisteredWaitHandle waitHandle);
            }
        }

        public static bool TryRegister(CancellationTokenSource cts, int timeoutMSec)
        {
            if ((cts != null) && (timeoutMSec > 0) && !cts.IsCancellationRequested)
            {
                Unregister(cts);

                var ctsWaitHandle = cts.Token.WaitHandle;
                if (!(ctsWaitHandle.SafeWaitHandle?.IsClosed ?? true))
                {
                    _timeoutRegisterations[cts] = 
                        ThreadPool.RegisterWaitForSingleObject(ctsWaitHandle, Callback, cts, timeoutMSec, true);
                    return true;
                }
            }
            return false;
        }

        public static void Unregister(CancellationTokenSource cts)
        {
            if (cts != null &&
                _timeoutRegisterations.TryRemove(cts, out RegisteredWaitHandle waitHandle))
                waitHandle.Unregister(cts.Token.WaitHandle);
        }
    }
}
