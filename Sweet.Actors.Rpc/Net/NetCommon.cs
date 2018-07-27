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

namespace Sweet.Actors.Rpc
{
    internal static class NetCommon
    {
        #region General
        internal static bool IsEmpty(this ServerEndPoint endPoint)
        {
            return (endPoint is null || endPoint.IsEmpty);
        }

        #endregion General

        #region Sockets

        internal static void Configure(this Socket socket, int? sendTimeoutMSec, int? receiveTimeoutMSec, bool noDelay, bool keepAlive)
        {
            if (socket != null)
            {
                var nativeSocket = socket as NativeSocket;
                if (nativeSocket == null || !nativeSocket.Disposed)
                {
                    socket.SetIOLoopbackFastPath();

                    if (sendTimeoutMSec.HasValue && sendTimeoutMSec > 0)
                    {
                        socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.SendTimeout,
                                                sendTimeoutMSec == int.MaxValue ? Timeout.Infinite : sendTimeoutMSec.Value);
                    }

                    if (receiveTimeoutMSec.HasValue && receiveTimeoutMSec > 0)
                    {
                        socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveTimeout,
                                                receiveTimeoutMSec == int.MaxValue ? Timeout.Infinite : receiveTimeoutMSec.Value);
                    }

                    socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, keepAlive);

                    socket.NoDelay = noDelay;
                }
            }
        }

        internal static bool IsSocketError(this SocketError errorCode)
        {
            return !(errorCode == SocketError.Success || 
                errorCode == SocketError.IOPending ||
                errorCode == SocketError.WouldBlock);
        }

        internal static void SetIOLoopbackFastPath(this Socket socket)
        {
            if (Common.IsWinPlatform)
            {
                try
                {
                    var ops = BitConverter.GetBytes(1);
                    socket.IOControl(NetConstants.SIO_LOOPBACK_FAST_PATH, ops, null);
                }
                catch (Exception)
                { }
            }
        }

        internal static bool IsConnected(this Socket socket, int poll = -1)
        {
            if ((socket != null) && socket.Connected && socket.Handle != IntPtr.Zero)
            {
                if (poll > -1)
                    return !(socket.Poll(poll, SelectMode.SelectRead) && (socket.Available == 0));
                return true;
            }
            return false;
        }

        #endregion Sockets
    }
}