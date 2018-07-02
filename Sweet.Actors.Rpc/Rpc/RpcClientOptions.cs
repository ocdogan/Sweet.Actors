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

namespace Sweet.Actors.Rpc
{
    public class RpcClientOptions : RpcOptions<RpcClientOptions>
    {
        public static readonly RpcClientOptions Default = new RpcClientOptions();

        private int _readBufferSize;
        private int _connectionTimeoutMSec = -1;

        public RpcClientOptions()
            : base()
        { }

        protected override RpcClientOptions New()
        {
            return new RpcClientOptions();
        }

        public RpcClientOptions UsingReadBufferSize(int size)
        {
            _readBufferSize = size;
            return this;
        }

        public RpcClientOptions UsingConnectionTimeoutMSec(int connectionTimeoutMSec)
        {
            if (connectionTimeoutMSec < 0)
                _connectionTimeoutMSec = -1;
            else if (connectionTimeoutMSec == 0)
                _connectionTimeoutMSec = RpcConstants.DefaultConnectionTimeout;
            else _connectionTimeoutMSec = Math.Min(RpcConstants.MaxConnectionTimeout, Math.Max(RpcConstants.MinConnectionTimeout, connectionTimeoutMSec));

            return this;
        }

        public int ConnectionTimeoutMSec => _connectionTimeoutMSec;

        public int ReadBufferSize => _readBufferSize;
    }
}
