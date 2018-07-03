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
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Sweet.Actors.Rpc
{
    internal static class NetAsyncEx
    {
        #region Methods

        #region Dns

        public static Task<IPAddress> GetHostAddressAsync(string host)
        {
            var tcs = new TaskCompletionSource<IPAddress>();

            Dns.BeginGetHostAddresses(host, ar =>
            {
                try
                {
                    var addresses = Dns.EndGetHostAddresses(ar);
                    tcs.TrySetResult(!addresses.IsEmpty() ? addresses[0] : null);
                }
                catch (OperationCanceledException)
                {
                    tcs.TrySetCanceled();
                }
                catch (Exception e)
                {
                    tcs.TrySetException(e);
                }
            }, tcs);
            return tcs.Task;
        }

        public static Task<IPAddress[]> GetHostAddressesAsync(string host)
        {
            var tcs = new TaskCompletionSource<IPAddress[]>();

            Dns.BeginGetHostAddresses(host, ar =>
            {
                try
                {
                    var addresses = Dns.EndGetHostAddresses(ar);
                    tcs.TrySetResult(addresses ?? new IPAddress[0]);
                }
                catch (OperationCanceledException)
                {
                    tcs.TrySetCanceled();
                }
                catch (Exception e)
                {
                    tcs.TrySetException(e);
                }
            }, tcs);
            return tcs.Task;
        }

        #endregion Dns

        #region Socket

        public static Task ConnectAsync(this Socket socket, IPEndPoint endPoint, int timeoutMSec = -1)
        {
            var tcs = new TaskCompletionSource<bool>();

            var asyncResult = socket.BeginConnect(endPoint, ar =>
            {
                try
                {
                    socket.EndConnect(ar);
                    tcs.TrySetResult(true);
                }
                catch (OperationCanceledException)
                {
                    tcs.TrySetCanceled();
                }
                catch (Exception e)
                {
                    tcs.TrySetException(e);
                }
                finally
                {
                    TimeoutHandler.Unregister(tcs);
                }
            }, tcs);

            RegisterForTimeout(tcs, timeoutMSec, socket, asyncResult);

            return tcs.Task;
        }

        private static void RegisterForTimeout(TaskCompletionSource<bool> tcs, int timeoutMSec, Socket socket, IAsyncResult asyncResult)
        {
            if (timeoutMSec > 0 && asyncResult != null)
            {
                TimeoutHandler.TryRegister(tcs, 
                    () =>
                    {
                        try
                        {
                            if (socket.IsConnected() &&
                                !asyncResult.IsCompleted)
                                socket.EndConnect(asyncResult);
                            else
                                socket.Close();
                        }
                        catch (Exception)
                        { }
                        finally
                        {
                            tcs.TrySetCanceled();
                        }
                    }, timeoutMSec);
            }
        }

        public static Task ConnectAsync(this Socket socket, EndPoint remoteEP, int timeoutMSec = -1)
        {
            var tcs = new TaskCompletionSource<bool>();

            var asyncResult = socket.BeginConnect(remoteEP, ar =>
            {
                try
                {
                    socket.EndConnect(ar);
                    tcs.TrySetResult(true);
                }
                catch (OperationCanceledException)
                {
                    tcs.TrySetCanceled();
                }
                catch (Exception e)
                {
                    tcs.TrySetException(e);
                }
                finally
                {
                    TimeoutHandler.Unregister(tcs);
                }
            }, tcs);

            RegisterForTimeout(tcs, timeoutMSec, socket, asyncResult);

            return tcs.Task;
        }

        public static Task ConnectAsync(this Socket socket, IPAddress address, int port, int timeoutMSec = -1)
        {
            return ConnectAsync(socket, new IPEndPoint(address, port), timeoutMSec);
        }

        public static Task ConnectAsync(this Socket socket, IPAddress[] addresses, int port, int timeoutMSec = -1)
        {
            var tcs = new TaskCompletionSource<bool>();

            var asyncResult = socket.BeginConnect(addresses, port, ar =>
            {
                try
                {
                    socket.EndConnect(ar);
                    tcs.TrySetResult(true);
                }
                catch (OperationCanceledException)
                {
                    tcs.TrySetCanceled();
                }
                catch (Exception e)
                {
                    tcs.TrySetException(e);
                }
                finally
                {
                    TimeoutHandler.Unregister(tcs);
                }
            }, tcs);

            RegisterForTimeout(tcs, timeoutMSec, socket, asyncResult);

            return tcs.Task;
        }

        public static Task ConnectAsync(this Socket socket, string host, int port, int timeoutMSec = -1)
        {
            var tcs = new TaskCompletionSource<bool>();

            var asyncResult = socket.BeginConnect(host, port, ar =>
            {
                try
                {
                    socket.EndConnect(ar);
                    tcs.TrySetResult(true);
                }
                catch (OperationCanceledException)
                {
                    tcs.TrySetCanceled();
                }
                catch (Exception e)
                {
                    tcs.TrySetException(e);
                }
                finally
                {
                    TimeoutHandler.Unregister(tcs);
                }
            }, tcs);

            RegisterForTimeout(tcs, timeoutMSec, socket, asyncResult);

            return tcs.Task;
        }

        public static Task DisconnectAsync(this Socket socket, bool reuseSocket = false)
        {
            var tcs = new TaskCompletionSource<bool>();

            socket.BeginDisconnect(reuseSocket, ar =>
            {
                try
                {
                    socket.EndDisconnect(ar);
                    tcs.TrySetResult(true);
                }
                catch (OperationCanceledException)
                {
                    tcs.TrySetCanceled();
                }
                catch (Exception e)
                {
                    tcs.TrySetException(e);
                }
            }, tcs);
            return tcs.Task;
        }

        public static Task<int> SendAsync(this Socket socket, byte[] data, int offset, int count, SocketFlags socketFlags = SocketFlags.None)
        {
            var tcs = new TaskCompletionSource<int>();

            socket.BeginSend(data, offset, count, socketFlags, ar =>
            {
                try
                {
                    tcs.TrySetResult(socket.EndSend(ar));
                }
                catch (OperationCanceledException)
                {
                    tcs.TrySetCanceled();
                }
                catch (Exception e)
                {
                    tcs.TrySetException(e);
                }
            }, tcs);
            return tcs.Task;
        }

        public static Task<int> ReceiveAsync(this Socket socket, byte[] data, int offset, int count, SocketFlags socketFlags = SocketFlags.None)
        {
            var tcs = new TaskCompletionSource<int>();

            socket.BeginReceive(data, offset, count, socketFlags, ar =>
            {
                try
                {
                    tcs.TrySetResult(socket.EndReceive(ar));
                }
                catch (OperationCanceledException)
                {
                    tcs.TrySetCanceled();
                }
                catch (Exception e)
                {
                    tcs.TrySetException(e);
                }
            }, tcs);
            return tcs.Task;
        }

        #endregion Socket

        #endregion Methods
    }
}