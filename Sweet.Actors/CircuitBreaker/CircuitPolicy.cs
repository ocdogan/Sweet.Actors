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
    public class CircuitPolicy
    {
        public static readonly CircuitPolicy DefaultPolicy = new CircuitPolicy(3, TimeSpan.FromSeconds(10000),
            TimeSpan.FromSeconds(1), 2, true);

        private bool _throwErrors;

        private int _failureCountToOpen;
        private int _failureTrackWindow;
        private int _keepOpenDuration;
        private int _successCountToClose;

        public CircuitPolicy(int failureCountToOpen, TimeSpan failureTrackWindow, 
            TimeSpan keepOpenDuration, int successCountToClose, bool throwErrors = true)
        {
            _throwErrors = throwErrors;
            _failureCountToOpen = Math.Max(1, failureCountToOpen);
            _successCountToClose = Math.Max(1, successCountToClose);
            _failureTrackWindow = Math.Max(1, Convert.ToInt32(failureTrackWindow.TotalMilliseconds));
            _keepOpenDuration = Math.Max(1, Convert.ToInt32(keepOpenDuration.TotalMilliseconds));
        }

        public bool ThrowErrors => _throwErrors;

        public int FailureCountToOpen => _failureCountToOpen;

        public int FailureTrackWindow => _failureTrackWindow;

        public int KeepOpenDuration => _keepOpenDuration;

        public int SuccessCountToClose => _successCountToClose;
    }
}
