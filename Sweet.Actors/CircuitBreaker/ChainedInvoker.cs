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

namespace Sweet.Actors
{
    public delegate object ChainedInvokerDelegate((object @return, bool success) prevResult, out bool success);

    public class ChainedInvoker : ICircuitInvoker
    {
        private ChainedInvokerDelegate _nextChain;

        public ChainedInvoker(ChainedInvokerDelegate next)
        {
            _nextChain = next;
        }

        public bool Execute(Action action)
        {
            action();

            var success = false;
            _nextChain?.Invoke((null, true), out success);
            return success;
        }

        public TResult Execute<TResult>(Func<TResult> function, out bool success)
        {
            success = true;
            var result = function();
            _nextChain?.Invoke((result, success), out success);
            return result;
        }
    }
}
