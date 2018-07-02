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
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

using Sweet.Actors;

namespace Sweet.Actors.Rpc
{
    internal static class NetAsyncEx
    {
        #region Methods

        #region Dns

        public static Task<IPAddress> GetHostAddressAsync(string host)
        {
            var tcs = new TaskCompletionSource<IPAddress>(null);
            Dns.BeginGetHostAddresses(host, ar =>
            {
                var innerTcs = ar.AsyncState as TaskCompletionSource<IPAddress>;
                try
                {
                    var addresses = Dns.EndGetHostAddresses(ar);
                    innerTcs.TrySetResult(!addresses.IsEmpty() ? addresses[0] : null);
                }
                catch (OperationCanceledException)
                {
                    innerTcs.TrySetCanceled();
                }
                catch (Exception e)
                {
                    innerTcs.TrySetException(e);
                }
            }, tcs);
            return tcs.Task;
        }

        public static Task<IPAddress[]> GetHostAddressesAsync(string host)
        {
            var tcs = new TaskCompletionSource<IPAddress[]>(null);
            Dns.BeginGetHostAddresses(host, ar =>
            {
                var innerTcs = ar.AsyncState as TaskCompletionSource<IPAddress[]>;
                try
                {
                    var addresses = Dns.EndGetHostAddresses(ar);
                    innerTcs.TrySetResult(addresses ?? new IPAddress[0]);
                }
                catch (OperationCanceledException)
                {
                    innerTcs.TrySetCanceled();
                }
                catch (Exception e)
                {
                    innerTcs.TrySetException(e);
                }
            }, tcs);
            return tcs.Task;
        }

        #endregion Dns

        #region Socket

        public static Task ConnectAsync(this Socket socket, IPEndPoint endPoint, int timeoutMSec = -1)
        {
            var tcs = new TaskCompletionSource<object>(socket);

            var asyncResult = socket.BeginConnect(endPoint, ar =>
            {
                var innerTcs = ar.AsyncState as TaskCompletionSource<object>;
                try
                {
                    ((Socket)innerTcs.Task.AsyncState).EndConnect(ar);
                    innerTcs.TrySetResult(null);
                }
                catch (OperationCanceledException)
                {
                    innerTcs.TrySetCanceled();
                }
                catch (Exception e)
                {
                    innerTcs.TrySetException(e);
                }
            }, tcs);

            if (timeoutMSec > 0 && asyncResult != null)
            {
                TimeoutHandler.TryRegister(asyncResult.AsyncWaitHandle, timeoutMSec,
                    () => {
                        try
                        {
                            if (socket.IsConnected() &&
                                !asyncResult.IsCompleted)
                                socket.EndConnect(asyncResult);
                            else
                                socket.Close();
                        }
                        finally
                        {
                            tcs.TrySetCanceled();
                        }
                    });
            }
            return tcs.Task;
        }

        public static Task ConnectAsync(this Socket socket, EndPoint remoteEP, int timeoutMSec = -1)
        {
            var tcs = new TaskCompletionSource<bool>(socket);

            var asyncResult = socket.BeginConnect(remoteEP, ar =>
            {
                var innerTcs = ar.AsyncState as TaskCompletionSource<bool>;
                try
                {
                    ((Socket)innerTcs.Task.AsyncState).EndConnect(ar);
                    innerTcs.TrySetResult(true);
                }
                catch (OperationCanceledException)
                {
                    innerTcs.TrySetCanceled();
                }
                catch (Exception e)
                {
                    innerTcs.TrySetException(e);
                }
            }, tcs);

            if (timeoutMSec > 0 && asyncResult != null)
            {
                TimeoutHandler.TryRegister(asyncResult.AsyncWaitHandle, timeoutMSec,
                    () => {
                        try
                        {
                            if (socket.IsConnected() &&
                                !asyncResult.IsCompleted)
                                socket.EndConnect(asyncResult);
                            else
                                socket.Close();
                        }
                        finally
                        {
                            tcs.TrySetCanceled();
                        }
                    });
            }
            return tcs.Task;
        }

        public static Task ConnectAsync(this Socket socket, IPAddress address, int port, int timeoutMSec = -1)
        {
            return ConnectAsync(socket, new IPEndPoint(address, port), timeoutMSec);
        }

        public static Task ConnectAsync(this Socket socket, IPAddress[] addresses, int port, int timeoutMSec = -1)
        {
            var tcs = new TaskCompletionSource<bool>(socket);

            var asyncResult = socket.BeginConnect(addresses, port, ar =>
            {
                var innerTcs = ar.AsyncState as TaskCompletionSource<bool>;
                try
                {
                    ((Socket)innerTcs.Task.AsyncState).EndConnect(ar);
                    innerTcs.TrySetResult(true);
                }
                catch (OperationCanceledException)
                {
                    innerTcs.TrySetCanceled();
                }
                catch (Exception e)
                {
                    innerTcs.TrySetException(e);
                }
            }, tcs);

            if (timeoutMSec > 0 && asyncResult != null)
            {
                TimeoutHandler.TryRegister(asyncResult.AsyncWaitHandle, timeoutMSec,
                    () => {
                        try
                        {
                            if (socket.IsConnected() &&
                                !asyncResult.IsCompleted)
                                socket.EndConnect(asyncResult);
                            else
                                socket.Close();
                        }
                        finally
                        {
                            tcs.TrySetCanceled();
                        }
                    });
            }
            return tcs.Task;
        }

        public static Task ConnectAsync(this Socket socket, string host, int port, int timeoutMSec = -1)
        {
            var tcs = new TaskCompletionSource<bool>(socket);

            var asyncResult = socket.BeginConnect(host, port, ar =>
            {
                var innerTcs = ar.AsyncState as TaskCompletionSource<bool>;
                try
                {
                    ((Socket)innerTcs.Task.AsyncState).EndConnect(ar);
                    innerTcs.TrySetResult(true);
                }
                catch (OperationCanceledException)
                {
                    innerTcs.TrySetCanceled();
                }
                catch (Exception e)
                {
                    innerTcs.TrySetException(e);
                }
            }, tcs);

            if (timeoutMSec > 0 && asyncResult != null)
            {
                TimeoutHandler.TryRegister(asyncResult.AsyncWaitHandle, timeoutMSec,
                    () => {
                        try
                        {
                            if (socket.IsConnected() &&
                                !asyncResult.IsCompleted)
                                socket.EndConnect(asyncResult);
                            else
                                socket.Close();
                        }
                        finally
                        {
                            tcs.TrySetCanceled();
                        }
                    });
            }
            return tcs.Task;
        }

        public static Task DisconnectAsync(this Socket socket, bool reuseSocket = false)
        {
            var tcs = new TaskCompletionSource<object>(socket);

            socket.BeginDisconnect(reuseSocket, ar =>
            {
                var innerTcs = ar.AsyncState as TaskCompletionSource<object>;
                try
                {
                    ((Socket)innerTcs.Task.AsyncState).EndDisconnect(ar);
                    innerTcs.TrySetResult(null);
                }
                catch (OperationCanceledException)
                {
                    innerTcs.TrySetCanceled();
                }
                catch (Exception e)
                {
                    innerTcs.TrySetException(e);
                }
            }, tcs);
            return tcs.Task;
        }

        public static Task<int> SendAsync(this Socket socket, byte[] data, int offset, int count, SocketFlags socketFlags = SocketFlags.None)
        {
            var tcs = new TaskCompletionSource<int>(socket);

            socket.BeginSend(data, offset, count, socketFlags, ar =>
            {
                var innerTcs = ar.AsyncState as TaskCompletionSource<int>;
                try
                {
                    innerTcs.TrySetResult(((Socket)innerTcs.Task.AsyncState).EndSend(ar));
                }
                catch (OperationCanceledException)
                {
                    innerTcs.TrySetCanceled();
                }
                catch (Exception e)
                {
                    innerTcs.TrySetException(e);
                }
            }, tcs);
            return tcs.Task;
        }

        public static Task<int> ReceiveAsync(this Socket socket, byte[] data, int offset, int count, SocketFlags socketFlags = SocketFlags.None)
        {
            var tcs = new TaskCompletionSource<int>(socket);

            socket.BeginReceive(data, offset, count, socketFlags, ar =>
            {
                var innerTcs = ar.AsyncState as TaskCompletionSource<int>;
                try
                {
                    innerTcs.TrySetResult(((Socket)innerTcs.Task.AsyncState).EndReceive(ar));
                }
                catch (OperationCanceledException)
                {
                    innerTcs.TrySetCanceled();
                }
                catch (Exception e)
                {
                    innerTcs.TrySetException(e);
                }
            }, tcs);
            return tcs.Task;
        }

        #endregion Socket

        #endregion Methods
    }
}