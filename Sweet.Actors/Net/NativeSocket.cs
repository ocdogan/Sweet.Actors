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
using System.Net.Sockets;
using System.Threading;

namespace Sweet.Actors
{
    public class NativeSocket : Socket
    {
        #region Field Members

        private int _disposed;

        #endregion Field Members

        #region .Ctors

        public NativeSocket(SocketInformation socketInformation)
            : base(socketInformation)
        { }

        public NativeSocket(AddressFamily addressFamily, SocketType socketType, ProtocolType protocolType)
            : base(addressFamily, socketType, protocolType)
        { }

        #endregion .Ctors

        #region Destructors

        protected override void Dispose(bool disposing)
        {
            Interlocked.Exchange(ref _disposed, Common.True);
            base.Dispose(disposing);
        }

        #endregion Destructors

        #region Properties

        public bool Disposed
        {
            get { return _disposed != 0; }
        }

        #endregion Properties

        #region Methods

        public virtual void ThrowIdDisposed()
        {
            if (Disposed)
                throw new ObjectDisposedException(GetType().Name);
        }

        #endregion Methods
    }
}