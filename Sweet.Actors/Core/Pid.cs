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
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Sweet.Actors
{
    public class Pid : Aid
    {
        private Process _process;

        internal Pid(Process process)
            : base()
        {
            _process = process;
            if (process != null)
            {
                SetActorSystem(process.System?.Name);
                SetActor(process.Name);
            }
        }

        internal Process Process => _process;

        public virtual Task Tell(object message, IDictionary<string, string> header = null, int timeoutMSec = -1)
        {
			return _process.Send(message, header, timeoutMSec);
        }

		public virtual Task<IFutureResponse<T>> Request<T>(object message, IDictionary<string, string> header = null, int timeoutMSec = -1)
        {
			return _process.Request<T>(message, header, timeoutMSec);
        }

        public override string ToString()
        {
            return base.ToString();
        }

        public override int GetHashCode()
        {
            return base.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            if (obj == null)
                return false;

            if (obj is Pid pid)
                return pid.Equals(obj);
            return false;
        }
    }
}
