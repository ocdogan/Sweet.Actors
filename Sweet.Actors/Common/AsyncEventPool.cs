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
        private static long _processing;
        private static ConcurrentQueue<Action> _queue = new ConcurrentQueue<Action>();

        private static void Schedule()
        {
            if (Interlocked.CompareExchange(ref _processing, 1L, 0L) == 0L)
            {
                Task.Factory.StartNew(ProcessQueue);
            }
        }

        private static void ProcessQueue()
        {
            try
            {
                var loopCount = 0;
                while (_queue.TryDequeue(out Action action) &&
                    (loopCount++ <= 500))
                {
                    try
                    {
                        action();
                    }
                    catch (Exception)
                    { }
                }
            }
            finally
            {
                Interlocked.CompareExchange(ref _processing, 0L, 1L);
                if (!_queue.IsEmpty)
                    Schedule();
            }
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
