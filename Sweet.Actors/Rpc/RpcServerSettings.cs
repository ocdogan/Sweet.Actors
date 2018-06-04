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
    public class RpcServerSettings : RpcSettings<RpcServerSettings>
    {
        public const int MinConcurrentConnectionsCount = 10;
		public const int DefaultConcurrentConnectionsCount = Constants.KB;

        private int _concurrentConnections = DefaultConcurrentConnectionsCount;

        public RpcServerSettings()
            : base()
        { }

        public int ConcurrentConnections => _concurrentConnections; 

        public RpcServerSettings UsingConcurrentConnections(int concurrentConnections)
        {
            _concurrentConnections = (concurrentConnections < 1) ? DefaultConcurrentConnectionsCount : 
                Math.Max(MinConcurrentConnectionsCount, concurrentConnections);
            return this;
        }

        protected override RpcServerSettings NewInstance()
        {
            return new RpcServerSettings();
        }

        public override RpcServerSettings Clone()
        {
            var result = (RpcServerSettings)base.Clone();
            result._concurrentConnections = _concurrentConnections;

            return result;
        }
    }
}
