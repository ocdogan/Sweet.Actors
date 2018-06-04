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
using System.Collections;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Sweet.Actors
{
    internal static class Common
    {
        #region Static Members

        public static readonly UTF8Encoding UTF8 = new UTF8Encoding(true);

        private static readonly Action<Task> IgnoreTaskContinuation = (task) => { var ignored = task.Exception; };

		public static readonly int ProcessId = Environment.TickCount;

        private static readonly byte Minus = (byte)'-';
        private static readonly byte ZeroBase = (byte)'0';

        private static readonly byte[] ShortMinValue = UTF8.GetBytes("-32768");
        private static readonly byte[] ShortMaxValue = UTF8.GetBytes("32767");

        private static readonly byte[] IntMinValue = UTF8.GetBytes("-2147483648");
        private static readonly byte[] IntMaxValue = UTF8.GetBytes("2147483647");

        private static readonly byte[] LongMinValue = UTF8.GetBytes("-9223372036854775808");
        private static readonly byte[] LongMaxValue = UTF8.GetBytes("9223372036854775807");

        private static readonly byte[] ULongMaxValue = UTF8.GetBytes("18446744073709551615");

        private static bool? s_IsWinPlatform;
        private static bool? s_IsLinuxPlatform;

        #endregion Static Members

        #region Constants

        private const byte CharMinus = (byte)'-';
        private const byte CharZero = (byte)'0';
        private const byte CharNine = (byte)'9';

        private const int IntStringLen = 11;
        private const int LongStringLen = 20;
        private const int ShortStringLen = 6;

        #endregion Constants

        public static bool IsWinPlatform
        {
            get
            {
                if (!s_IsWinPlatform.HasValue)
                {
                    var pid = Environment.OSVersion.Platform;
                    switch (pid)
                    {
                        case PlatformID.Win32NT:
                        case PlatformID.Win32S:
                        case PlatformID.Win32Windows:
                        case PlatformID.WinCE:
                            s_IsWinPlatform = true;
                            break;
                        default:
                            s_IsWinPlatform = false;
                            break;
                    }
                }
                return s_IsWinPlatform.Value;
            }
        }

        public static bool IsLinuxPlatform
        {
            get
            {
                if (!s_IsLinuxPlatform.HasValue)
                {
                    int p = (int)Environment.OSVersion.Platform;
                    s_IsLinuxPlatform = (p == 4) || (p == 6) || (p == 128);
                }
                return s_IsLinuxPlatform.Value;
            }
        }

        public static int ValidateSequentialInvokeLimit(int sequentialInvokeLimit)
        {
            if (sequentialInvokeLimit < 1)
                return -1;

            return Math.Min(Constants.MaxSequentialInvokeLimit,
                        Math.Max(Constants.MinSequentialInvokeLimit, sequentialInvokeLimit));
        }

        public static bool CompareAndSet(ref int value, int expectedValue, int newValue)
        {
            return Interlocked.CompareExchange(ref value, newValue, expectedValue) == expectedValue;
        }

        public static bool CompareAndSet(ref int value, bool expectedValue, bool newValue)
        {
            var @new = newValue ? Constants.True : Constants.False;
            var expected = expectedValue ? Constants.True : Constants.False;

            return Interlocked.CompareExchange(ref value, @new, expected) == expected;
       }

        public static bool CompareAndSet(ref long value, long expectedValue, long newValue)
        {
            return Interlocked.CompareExchange(ref value, newValue, expectedValue) == expectedValue;
        }

        public static bool CompareAndSet(ref long value, bool expectedValue, bool newValue)
        {
            var @new = newValue ? Constants.True : Constants.False;
            var expected = expectedValue ? Constants.True : Constants.False;

            return Interlocked.CompareExchange(ref value, @new, expected) == expected;
        }

        internal static bool IsEmpty(this string obj)
        {
            return (obj == null || obj.Length == 0);
        }

        internal static bool IsEmpty(this Array obj)
        {
            return (obj == null || obj.Length == 0);
        }

        internal static bool IsEmpty(this ICollection obj)
        {
            return (obj == null || obj.Count == 0);
        }

        internal static bool IsEmpty(this ServerEndPoint endPoint)
        {
            return (endPoint is null || endPoint.IsEmpty);
        }

		internal static void Ignore(this Task task)
        {
            if (task.IsCompleted)
                IgnoreTaskContinuation(task);
            else
            {
                task.ContinueWith(
                    IgnoreTaskContinuation,
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
        }

        internal static byte[] Clone(this byte[] bytes, int offset = 0, int length = -1)
        {
            if (bytes != null)
            {
                var bytesLength = bytes.Length;
                if (offset < bytesLength)
                {
                    if (bytesLength == 0)
                        return new byte[0];

                    if (offset < 0) offset = 0;

                    if (length < 0) length = bytesLength;

                    length = Math.Min(length, bytesLength - offset);
                    if (length > -1)
                    {
                        var result = new byte[length];
                        if (length == 0)
                            return result;

                        Array.Copy(bytes, offset, result, 0, length);
                        return result;
                    }
                }
            }
            return null;
        }

        #region Sockets

        internal static void SetIOLoopbackFastPath(this Socket socket)
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

        internal static bool IsConnected(this Socket socket, int poll = -1)
        {
            if (socket != null && socket.Connected)
            {
                if (poll > -1)
                    return !(socket.Poll(poll, SelectMode.SelectRead) && (socket.Available == 0));
                return true;
            }
            return false;
        }

        #endregion Sockets

        #region ToBytes

        internal static byte[] ToBytes(this string obj)
        {
            return (obj != null) ? UTF8.GetBytes(obj) : null;
        }

        internal static byte[] ToBytes(this byte[] obj)
        {
            return obj;
        }

        internal static byte[] ToBytes(this short value)
        {
            var minus = (value < 0);
            if (minus)
            {
                if (value == short.MinValue)
                    return (byte[])ShortMinValue.Clone();

                value = (short)-value;

                if (value < 10)
                    return new byte[] { Minus, (byte)(value + ZeroBase) };

                if (value < 100)
                    return new byte[] { Minus, (byte)((value / 10) + ZeroBase), (byte)((value % 10) + ZeroBase) };

                if (value < 1000)
                    return new byte[] { (byte)((value / 100) + ZeroBase), (byte)(((value / 10) + ZeroBase) % 10), (byte)((value % 10) + ZeroBase) };
            }
            else
            {
                if (value < 10)
                    return new byte[] { (byte)(value + ZeroBase) };

                if (value < 100)
                    return new byte[] { (byte)((value / 10) + ZeroBase), (byte)((value % 10) + ZeroBase) };

                if (value < 1000)
                    return new byte[] { (byte)((value / 100) + ZeroBase), (byte)(((value / 10) + ZeroBase) % 10), (byte)((value % 10) + ZeroBase) };

                if (value == short.MaxValue)
                    return (byte[])ShortMaxValue.Clone();
            }

            var bytes = BytesCache.Acquire(ShortStringLen);

            byte mod;
            var index = IntStringLen;

            do
            {
                mod = (byte)((value % 10) + ZeroBase);
                value /= 10;

                bytes[--index] = mod;
            } while (value > 0);

            if (minus)
                bytes[--index] = Minus;

            var result = (byte[])bytes.Clone(index, IntStringLen - index);
            BytesCache.Release(bytes);

            return result;
        }

        internal static byte[] ToBytes(this int value)
        {
            var minus = (value < 0);
            if (minus)
            {
                if (value == int.MinValue)
                    return (byte[])IntMinValue.Clone();

                value = -value;

                if (value < 10)
                    return new byte[] { Minus, (byte)(value + ZeroBase) };

                if (value < 100)
                    return new byte[] { Minus, (byte)((value / 10) + ZeroBase), (byte)((value % 10) + ZeroBase) };

                if (value < 1000)
                    return new byte[] { (byte)((value / 100) + ZeroBase), (byte)(((value / 10) + ZeroBase) % 10), (byte)((value % 10) + ZeroBase) };
            }
            else
            {
                if (value < 10)
                    return new byte[] { (byte)(value + ZeroBase) };

                if (value < 100)
                    return new byte[] { (byte)((value / 10) + ZeroBase), (byte)((value % 10) + ZeroBase) };

                if (value < 1000)
                    return new byte[] { (byte)((value / 100) + ZeroBase), (byte)(((value / 10) + ZeroBase) % 10), (byte)((value % 10) + ZeroBase) };

                if (value == int.MaxValue)
                    return (byte[])IntMaxValue.Clone();
            }

            var bytes = BytesCache.Acquire(IntStringLen);

            byte mod;
            var index = IntStringLen;

            do
            {
                mod = (byte)((value % 10) + ZeroBase);
                value /= 10;

                bytes[--index] = mod;
            } while (value > 0);

            if (minus)
                bytes[--index] = Minus;

            var result = (byte[])bytes.Clone(index, IntStringLen - index);
            BytesCache.Release(bytes);

            return result;
        }

        internal static byte[] ToBytes(this long value)
        {
            var minus = (value < 0);
            if (minus)
            {
                if (value == long.MinValue)
                    return (byte[])LongMinValue.Clone();

                value = -value;

                if (value < 10L)
                    return new byte[] { Minus, (byte)(value + ZeroBase) };

                if (value < 100L)
                    return new byte[] { Minus, (byte)((value / 10L) + ZeroBase), (byte)((value % 10L) + ZeroBase) };

                if (value < 1000L)
                    return new byte[] { (byte)((value / 100L) + ZeroBase), (byte)(((value / 10L) + ZeroBase) % 10L), (byte)((value % 10L) + ZeroBase) };
            }
            else
            {
                if (value < 10L)
                    return new byte[] { (byte)(value + ZeroBase) };

                if (value < 100L)
                    return new byte[] { (byte)((value / 10L) + ZeroBase), (byte)((value % 10L) + ZeroBase) };

                if (value < 1000L)
                    return new byte[] { (byte)((value / 100L) + ZeroBase), (byte)(((value / 10L) + ZeroBase) % 10L), (byte)((value % 10L) + ZeroBase) };

                if (value == long.MaxValue)
                    return (byte[])LongMaxValue.Clone();
            }

            var bytes = BytesCache.Acquire(LongStringLen);

            byte mod;
            var index = LongStringLen;

            do
            {
                mod = (byte)((value % 10L) + ZeroBase);
                value /= 10L;

                bytes[--index] = mod;
            } while (value > 0);

            if (minus)
                bytes[--index] = Minus;

            var result = (byte[])bytes.Clone(index, IntStringLen - index);
            BytesCache.Release(bytes);

            return result;
        }

        internal static byte[] ToBytes(this ulong value)
        {
            if (value < 10UL)
                return new byte[] { (byte)(value + ZeroBase) };

            if (value < 100UL)
                return new byte[] { (byte)(value / 10UL + ZeroBase), (byte)(value % 10UL + ZeroBase) };

            if (value < 1000UL)
                return new byte[] { (byte)(value / 100UL + ZeroBase), (byte)((value / 10UL + ZeroBase) % 10UL), (byte)(value % 10UL + ZeroBase) };

            if (value == ulong.MaxValue)
                return (byte[])ULongMaxValue.Clone();

            var bytes = BytesCache.Acquire(LongStringLen);

            byte mod;
            var index = LongStringLen;

            do
            {
                mod = (byte)((value % 10L) + ZeroBase);
                value /= 10L;

                bytes[--index] = mod;
            } while (value > 0);

            var result = (byte[])bytes.Clone(index, IntStringLen - index);
            BytesCache.Release(bytes);

            return result;
        }

        internal static byte[] ToBytes(this ushort obj)
        {
            return ToBytes((int)obj);
        }

        internal static byte[] ToBytes(this uint obj)
        {
            return ToBytes((long)obj);
        }

        internal static byte[] ToBytes(this byte obj)
        {
            return new byte[] { obj };
        }

        internal static byte[] ToBytes(this decimal obj)
        {
            return UTF8.GetBytes(obj.ToString(Constants.InvariantCulture));
        }

        internal static byte[] ToBytes(this double obj)
        {
            return UTF8.GetBytes(obj.ToString(Constants.InvariantCulture));
        }

        internal static byte[] ToBytes(this float obj)
        {
            return UTF8.GetBytes(obj.ToString(Constants.InvariantCulture));
        }

        internal static byte[] ToBytes(this DateTime obj)
        {
            return UTF8.GetBytes(obj.Ticks.ToString(Constants.InvariantCulture));
        }

        internal static byte[] ToBytes(this char obj)
        {
            return UTF8.GetBytes(new char[] { obj });
        }

        internal static byte[] ToBytes(this object obj)
        {
            if (obj != null)
            {
                var tc = Type.GetTypeCode(obj.GetType());
                switch (tc)
                {
                    case TypeCode.Object:
                        if (obj is byte[])
                            return (byte[])obj;
                        return UTF8.GetBytes(obj.ToString());
                    case TypeCode.String:
                        return UTF8.GetBytes((string)obj);
                    case TypeCode.Int32:
                        return UTF8.GetBytes(((int)obj).ToString(Constants.InvariantCulture));
                    case TypeCode.Int64:
                        return UTF8.GetBytes(((long)obj).ToString(Constants.InvariantCulture));
                    case TypeCode.Decimal:
                        return UTF8.GetBytes(((decimal)obj).ToString(Constants.InvariantCulture));
                    case TypeCode.Double:
                        return UTF8.GetBytes(((double)obj).ToString(Constants.InvariantCulture));
                    case TypeCode.Boolean:
                        return UTF8.GetBytes((bool)obj ? Boolean.TrueString : Boolean.FalseString);
                    case TypeCode.Single:
                        return UTF8.GetBytes(((float)obj).ToString(Constants.InvariantCulture));
                    case TypeCode.Int16:
                        return UTF8.GetBytes(((short)obj).ToString(Constants.InvariantCulture));
                    case TypeCode.UInt32:
                        return UTF8.GetBytes(((uint)obj).ToString(Constants.InvariantCulture));
                    case TypeCode.UInt64:
                        return UTF8.GetBytes(((ulong)obj).ToString(Constants.InvariantCulture));
                    case TypeCode.UInt16:
                        return UTF8.GetBytes(((ushort)obj).ToString(Constants.InvariantCulture));
                    case TypeCode.DateTime:
                        return UTF8.GetBytes(((DateTime)obj).Ticks.ToString(Constants.InvariantCulture));
                    case TypeCode.Char:
                        return UTF8.GetBytes(new char[] { (char)obj });
                    case TypeCode.Byte:
                        return new byte[] { (byte)obj };
                    default:
                        break;
                }
            }
            return null;
        }

        #endregion ToBytes
    }
}
