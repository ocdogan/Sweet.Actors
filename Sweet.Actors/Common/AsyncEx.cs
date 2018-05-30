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

namespace Sweet.Actors
{
    internal static class AsyncEx
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

        public static Task ConnectAsync(this Socket socket, IPEndPoint endPoint)
        {
            var tcs = new TaskCompletionSource<object>(socket);

            socket.BeginConnect(endPoint, ar =>
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
            return tcs.Task;
        }

        public static Task ConnectAsync(this Socket socket, EndPoint remoteEP)
        {
            var tcs = new TaskCompletionSource<bool>(socket);
            socket.BeginConnect(remoteEP, ar =>
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
            return tcs.Task;
        }

        public static Task ConnectAsync(this Socket socket, IPAddress address, int port)
        {
            var tcs = new TaskCompletionSource<bool>(socket);
            socket.BeginConnect(address, port, ar =>
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
            return tcs.Task;
        }

        public static Task ConnectAsync(this Socket socket, IPAddress[] addresses, int port)
        {
            var tcs = new TaskCompletionSource<bool>(socket);
            socket.BeginConnect(addresses, port, ar =>
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
            return tcs.Task;
        }

        public static Task ConnectAsync(this Socket socket, string host, int port)
        {
            var tcs = new TaskCompletionSource<bool>(socket);
            socket.BeginConnect(host, port, ar =>
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

        #region Stream

        public static Task<bool> WriteAsync(this Stream stream, byte[] data, int offset, int count)
        {
            var tcs = new TaskCompletionSource<bool>(stream);

            stream.BeginWrite(data, offset, count, ar =>
            {
                var innerTcs = ar.AsyncState as TaskCompletionSource<bool>;
                try
                {
                    ((Stream)innerTcs.Task.AsyncState).EndWrite(ar);
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
            return tcs.Task;
        }

        public static Task<int> ReadAsync(this Stream stream, byte[] data, int offset, int count)
        {
            var tcs = new TaskCompletionSource<int>(stream);

            stream.BeginRead(data, offset, count, ar =>
            {
                var innerTcs = ar.AsyncState as TaskCompletionSource<int>;
                try
                {
                    innerTcs.TrySetResult(((Stream)innerTcs.Task.AsyncState).EndRead(ar));
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

        #endregion Stream

        #region Generic

        public static Task InvokeAsync(this Action action)
        {
            var tcs = new TaskCompletionSource<object>(null);

            action.BeginInvoke(ar =>
            {
                var innerTcs = ar.AsyncState as TaskCompletionSource<object>;
                try
                {
                    action.EndInvoke(ar);
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

        public static Task InvokeAsync<T>(this Action<T> action, T arg1)
        {
            var tcs = new TaskCompletionSource<object>(null);

            action.BeginInvoke(arg1, ar =>
            {
                var innerTcs = ar.AsyncState as TaskCompletionSource<object>;
                try
                {
                    action.EndInvoke(ar);
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

        public static Task InvokeAsync<T1, T2>(this Action<T1, T2> action, T1 arg1, T2 arg2)
        {
            var tcs = new TaskCompletionSource<object>(null);

            action.BeginInvoke(arg1, arg2, ar =>
            {
                var innerTcs = ar.AsyncState as TaskCompletionSource<object>;
                try
                {
                    action.EndInvoke(ar);
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

        public static Task InvokeAsync<T1, T2, T3>(this Action<T1, T2, T3> action, T1 arg1, T2 arg2, T3 arg3)
        {
            var tcs = new TaskCompletionSource<object>(null);

            action.BeginInvoke(arg1, arg2, arg3, ar =>
            {
                var innerTcs = ar.AsyncState as TaskCompletionSource<object>;
                try
                {
                    action.EndInvoke(ar);
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

        public static Task InvokeAsync<T1, T2, T3, T4>(this Action<T1, T2, T3, T4> action, T1 arg1, T2 arg2, T3 arg3, T4 arg4)
        {
            var tcs = new TaskCompletionSource<object>(null);

            action.BeginInvoke(arg1, arg2, arg3, arg4, ar =>
            {
                var innerTcs = ar.AsyncState as TaskCompletionSource<object>;
                try
                {
                    action.EndInvoke(ar);
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

        public static Task InvokeAsync<T1, T2, T3, T4, T5>(this Action<T1, T2, T3, T4, T5> action, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5)
        {
            var tcs = new TaskCompletionSource<object>(null);

            action.BeginInvoke(arg1, arg2, arg3, arg4, arg5, ar =>
            {
                var innerTcs = ar.AsyncState as TaskCompletionSource<object>;
                try
                {
                    action.EndInvoke(ar);
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

        public static Task InvokeAsync<T1, T2, T3, T4, T5, T6>(this Action<T1, T2, T3, T4, T5, T6> action, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6)
        {
            var tcs = new TaskCompletionSource<object>(null);

            action.BeginInvoke(arg1, arg2, arg3, arg4, arg5, arg6, ar =>
            {
                var innerTcs = ar.AsyncState as TaskCompletionSource<object>;
                try
                {
                    action.EndInvoke(ar);
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

        public static Task InvokeAsync<T1, T2, T3, T4, T5, T6, T7>(this Action<T1, T2, T3, T4, T5, T6, T7> action, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7)
        {
            var tcs = new TaskCompletionSource<object>(null);

            action.BeginInvoke(arg1, arg2, arg3, arg4, arg5, arg6, arg7, ar =>
            {
                var innerTcs = ar.AsyncState as TaskCompletionSource<object>;
                try
                {
                    action.EndInvoke(ar);
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

        public static Task<T> InvokeAsync<T>(this Func<T> action)
        {
            var tcs = new TaskCompletionSource<T>(default(T));

            action.BeginInvoke(ar =>
            {
                var innerTcs = ar.AsyncState as TaskCompletionSource<T>;
                try
                {
                    var result = action.EndInvoke(ar);
                    innerTcs.TrySetResult(result);
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

        public static Task<K> InvokeAsync<T, K>(this Func<T, K> action, T arg1)
        {
            var tcs = new TaskCompletionSource<K>(default(K));

            action.BeginInvoke(arg1, ar =>
            {
                var innerTcs = ar.AsyncState as TaskCompletionSource<K>;
                try
                {
                    var result = action.EndInvoke(ar);
                    innerTcs.TrySetResult(result);
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

        public static Task<K> InvokeAsync<T1, T2, K>(this Func<T1, T2, K> action, T1 arg1, T2 arg2)
        {
            var tcs = new TaskCompletionSource<K>(default(K));

            action.BeginInvoke(arg1, arg2, ar =>
            {
                var innerTcs = ar.AsyncState as TaskCompletionSource<K>;
                try
                {
                    var result = action.EndInvoke(ar);
                    innerTcs.TrySetResult(result);
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

        public static Task<K> InvokeAsync<T1, T2, T3, K>(this Func<T1, T2, T3, K> action, T1 arg1, T2 arg2, T3 arg3)
        {
            var tcs = new TaskCompletionSource<K>(default(K));

            action.BeginInvoke(arg1, arg2, arg3, ar =>
            {
                var innerTcs = ar.AsyncState as TaskCompletionSource<K>;
                try
                {
                    var result = action.EndInvoke(ar);
                    innerTcs.TrySetResult(result);
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

        public static Task<K> InvokeAsync<T1, T2, T3, T4, K>(this Func<T1, T2, T3, T4, K> action, T1 arg1, T2 arg2, T3 arg3, T4 arg4)
        {
            var tcs = new TaskCompletionSource<K>(default(K));

            action.BeginInvoke(arg1, arg2, arg3, arg4, ar =>
            {
                var innerTcs = ar.AsyncState as TaskCompletionSource<K>;
                try
                {
                    var result = action.EndInvoke(ar);
                    innerTcs.TrySetResult(result);
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

        public static Task<K> InvokeAsync<T1, T2, T3, T4, T5, K>(this Func<T1, T2, T3, T4, T5, K> action, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5)
        {
            var tcs = new TaskCompletionSource<K>(default(K));

            action.BeginInvoke(arg1, arg2, arg3, arg4, arg5, ar =>
            {
                var innerTcs = ar.AsyncState as TaskCompletionSource<K>;
                try
                {
                    var result = action.EndInvoke(ar);
                    innerTcs.TrySetResult(result);
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

        public static Task<K> InvokeAsync<T1, T2, T3, T4, T5, T6, K>(this Func<T1, T2, T3, T4, T5, T6, K> action, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6)
        {
            var tcs = new TaskCompletionSource<K>(default(K));

            action.BeginInvoke(arg1, arg2, arg3, arg4, arg5, arg6, ar =>
            {
                var innerTcs = ar.AsyncState as TaskCompletionSource<K>;
                try
                {
                    var result = action.EndInvoke(ar);
                    innerTcs.TrySetResult(result);
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

        public static Task<K> InvokeAsync<T1, T2, T3, T4, T5, T6, T7, K>(this Func<T1, T2, T3, T4, T5, T6, T7, K> action, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7)
        {
            var tcs = new TaskCompletionSource<K>(default(K));

            action.BeginInvoke(arg1, arg2, arg3, arg4, arg5, arg6, arg7, ar =>
            {
                var innerTcs = ar.AsyncState as TaskCompletionSource<K>;
                try
                {
                    var result = action.EndInvoke(ar);
                    innerTcs.TrySetResult(result);
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

        #endregion Generic

        #endregion Methods
    }
}