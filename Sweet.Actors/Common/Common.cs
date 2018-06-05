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

        internal static byte[] ToBytes(this string value)
        {
            return (value != null) ? UTF8.GetBytes(value) : null;
        }

        internal static byte[] ToBytes(this byte[] value)
        {
            return value;
        }

        internal static byte[] ToBytes(this short value)
        {
            return BitConverter.GetBytes(value);
        }

        internal static byte[] ToBytes(this int value)
        {
            return BitConverter.GetBytes(value);
        }

        internal static byte[] ToBytes(this long value)
        {
            return BitConverter.GetBytes(value);
        }

        internal static byte[] ToBytes(this ulong value)
        {
            return BitConverter.GetBytes(value);
        }

        internal static byte[] ToBytes(this ushort value)
        {
            return BitConverter.GetBytes(value);
        }

        internal static byte[] ToBytes(this uint value)
        {
            return BitConverter.GetBytes(value);
        }

        internal static byte[] ToBytes(this byte value)
        {
            return new byte[] { value };
        }

        internal static byte[] ToBytes(this decimal value)
        {
            return BitConverter.GetBytes(Convert.ToDouble(value));
        }

        internal static byte[] ToBytes(this double value)
        {
            return BitConverter.GetBytes(value);
        }

        internal static byte[] ToBytes(this float value)
        {
            return BitConverter.GetBytes(value);
        }

        internal static byte[] ToBytes(this DateTime value)
        {
            var ticks = BitConverter.GetBytes(value.Ticks);

            var result = new byte[ticks.Length + 1];
            Array.Copy(ticks, result, ticks.Length);
            
            result[result.Length -1] = (byte)value.Kind;
            return result;
        }

        internal static byte[] ToBytes(this char value)
        {
            return BitConverter.GetBytes(value);
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
                        return BitConverter.GetBytes((int)obj);
                    case TypeCode.Int64:
                        return BitConverter.GetBytes((long)obj);
                    case TypeCode.Decimal:
                        return BitConverter.GetBytes(Convert.ToDouble((decimal)obj));
                    case TypeCode.Double:
                        return BitConverter.GetBytes((double)obj);
                    case TypeCode.Boolean:
                        return BitConverter.GetBytes((bool)obj);
                    case TypeCode.Single:
                        return BitConverter.GetBytes((float)obj);
                    case TypeCode.Int16:
                        return BitConverter.GetBytes((short)obj);
                    case TypeCode.UInt32:
                        return BitConverter.GetBytes((uint)obj);
                    case TypeCode.UInt64:
                        return BitConverter.GetBytes((ulong)obj);
                    case TypeCode.UInt16:
                        return BitConverter.GetBytes((ushort)obj);
                    case TypeCode.DateTime:
                        return BitConverter.GetBytes(((DateTime)obj).Ticks);
                    case TypeCode.Char:
                        return BitConverter.GetBytes((char)obj);
                    case TypeCode.Byte:
                        return new byte[] { (byte)obj };
                    default:
                        break;
                }
            }
            return null;
        }

        #endregion ToBytes

        #region FromBytes

        internal static string ToString(this byte[] value, int index, int count)
        {
            return (value != null) ? UTF8.GetString(value, index, count) : null;
        }

        internal static short ToShort(this byte[] value, int index)
        {
            return BitConverter.ToInt16(value, index);
        }

        internal static int ToInt(this byte[] value, int index)
        {
            return BitConverter.ToInt32(value, index);
        }

        internal static long ToLong(this byte[] value, int index)
        {
            return BitConverter.ToInt64(value, index);
        }

        internal static ulong ToULong(this byte[] value, int index)
        {
            return BitConverter.ToUInt64(value, index);
        }

        internal static ushort ToUShort(this byte[] value, int index)
        {
            return BitConverter.ToUInt16(value, index);
        }

        internal static uint ToUInt(this byte[] value, int index)
        {
            return BitConverter.ToUInt32(value, index);
        }

        internal static decimal ToDecimal(this byte[] value, int index)
        {
            var dbl = BitConverter.ToDouble(value, index);
            return Convert.ToDecimal(dbl);
        }

        internal static double ToDouble(this byte[] value, int index)
        {
            return BitConverter.ToDouble(value, index);
        }

        internal static float ToFloat(this byte[] value, int index)
        {
            return BitConverter.ToSingle(value, index);
        }

        internal static DateTime? ToDateTime(this byte[] value, int index)
        {
            var ticks = BitConverter.ToInt64(value, index);
            if (value.Length >= index + 9)
            {
                var kind = (DateTimeKind)((int)value[index + 8]);
                return new DateTime(ticks, kind);
            }
            return null;
        }

        internal static char ToChar(this byte[] value, int index)
        {
            return BitConverter.ToChar(value, index);
        }

        #endregion FromBytes
    }
}
