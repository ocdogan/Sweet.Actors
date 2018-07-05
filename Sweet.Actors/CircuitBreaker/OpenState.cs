﻿#region License
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
    internal class OpenState : CircuitState
    {
        private int _enteredTime;

        public OpenState(CircuitBreaker circuitBreaker, CircuitPolicy policy)
            : base(circuitBreaker, policy)
        { }

        public override CircuitStatus Status => CircuitStatus.Closed;

        protected override bool OnExecute(Action action)
        {
            if (!Policy.ThrowErrors)
                return false;
            throw new Exception(CircuitErrors.CircuitIsClosed);
        }

        public override void Entered()
        {
            _enteredTime = Environment.TickCount;
        }

        protected override void OnFail(Exception exception)
        {
            if (Environment.TickCount - _enteredTime >= Policy.KeepOpenDuration)
            {
                _enteredTime = 0;
                CircuitBreaker.SwitchToState(CircuitStatus.Closed);
            }
        }

        protected override void OnSucceed()
        { }
    }
}
