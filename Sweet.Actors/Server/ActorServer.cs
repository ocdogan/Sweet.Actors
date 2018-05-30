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
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Sweet.Actors
{
    public class ActorServer : Disposable
    {
        private Socket _socket;
        private ServerEndPoint _endPoint;

        public ActorServer(ServerEndPoint endPoint)
        {
             _endPoint = endPoint ?? throw new ArgumentNullException(nameof(endPoint));
        }

        public ServerEndPoint EndPoint => _endPoint;

        public void Start()
        {
            Stop();

            var socket = new NativeSocket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
                                { Blocking = false };
            SetIOLoopbackFastPath(socket);

        }

        public void Stop()
        {
            var socket = Interlocked.Exchange(ref _socket, null);
            if (socket != null)
            {
                using (socket)
                    socket.Close();
            }
        }

        private void SetIOLoopbackFastPath(Socket socket)
        {
            if (Common.IsWinPlatform)
            {
                try
                {
                    var ops = BitConverter.GetBytes(1);
                    socket.IOControl(Constants.SIO_LOOPBACK_FAST_PATH, ops, null);
                }
                catch (Exception)
                { }
            }
        }
    }
}
