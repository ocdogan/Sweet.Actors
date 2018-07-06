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
    public static class AsyncEventPool
    {
        private const int WaitDuration = 5000;

        private static int _inProcess;
        private static readonly ManualResetEventSlim _resetEvent = new ManualResetEventSlim(false);

        private static readonly ConcurrentQueue<Action> _queue = new ConcurrentQueue<Action>();

        private static void Schedule()
        {
            if (Interlocked.CompareExchange(ref _inProcess, 1, 0) == 0)
                Task.Factory.StartNew(ProcessQueue);
            else if (!_resetEvent.IsSet)
                _resetEvent.Set();
        }

        private static void ProcessQueue()
        {
            try
            {
                DequeueOrWait();
            }
            finally
            {
                _resetEvent.Reset();
                Interlocked.CompareExchange(ref _inProcess, 0, 1);

                if (!_queue.IsEmpty)
                    Schedule();
            }
        }

        private static void DequeueOrWait()
        {
            do
            {
                var loopCount = 0;
                try
                {
                    while (_queue.TryDequeue(out Action action))
                    {
                        try
                        {
                            action();
                        }
                        catch (Exception)
                        { }

                        if (loopCount++ >= 100)
                        {
                            loopCount = 0;
                            Thread.Sleep(1);
                        }
                    }
                }
                finally
                {
                    _resetEvent.Reset();
                }
            }
            while (_resetEvent.Wait(WaitDuration));
        }

        public static void Run(Action action)
        {
            if (action != null)
            {
                _queue.Enqueue(action);
                Schedule();
            }
        }
    }
}
