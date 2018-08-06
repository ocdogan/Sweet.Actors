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
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

//ADD THE ATTRIBUTE WITH NAME OF THE ASSEMBLY
[assembly: InternalsVisibleTo("Sweet.Actors.Rpc")]

namespace Sweet.Actors
{
    internal static class Common
    {
        #region Static Members

        public static readonly UTF8Encoding UTF8 = new UTF8Encoding(true);

        private static readonly Action<Task> IgnoreTaskContinuation = (task) => { var ignored = task.Exception; };

		public static readonly int ProcessId = Math.Abs(Environment.TickCount);

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

        #region Platform

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

        #endregion Platform

        public static int CheckSequentialInvokeLimit(int sequentialInvokeLimit)
        {
            if (sequentialInvokeLimit < 1)
                return -1;

            return Math.Min(Constants.MaxSequentialInvokeLimit,
                        Math.Max(Constants.MinSequentialInvokeLimit, sequentialInvokeLimit));
        }

        public static int? CheckMessageTimeout(int? timeoutMSec)
        {
            if (timeoutMSec.HasValue)
            {
                if (timeoutMSec < 0)
                    return Constants.MaxRequestTimeoutMSec;

                if (timeoutMSec == 0)
                    return Constants.DefaultRequestTimeoutMSec;

                return Math.Min(Constants.MaxRequestTimeoutMSec, timeoutMSec.Value);
            }
            return timeoutMSec;
        }

        #region Atomic

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

        #endregion Atomic

        #region IsEmpty

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

        #endregion IsEmpty

        #region Threading.Task

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

        #endregion Threading.Task

        #region Bytes

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

                        Buffer.BlockCopy(bytes, offset, result, 0, length);
                        return result;
                    }
                }
            }
            return null;
        }

        internal static bool EqualTo(this byte[] bytesX, byte[] bytesY)
        {
            if (ReferenceEquals(bytesX, bytesY))
                return true;

            if (bytesX is null)
                return (bytesY is null);

            if (bytesY is null)
                return false;

            var l1 = bytesX.Length;
            var l2 = bytesY.Length;

            if (l1 != l2)
                return false;

            for (var i = 0; i < l1; i++)
                if (bytesX[i] != bytesY[i])
                    return false;

            return true;
        }

        internal static string ToUTF8String(this byte[] bytes, int offset = 0, int length = -1)
        {
            if (bytes != null)
            {
                var bytesLength = bytes.Length;
                if (offset < bytesLength)
                {
                    if (bytesLength == 0)
                        return String.Empty;

                    if (offset < 0) offset = 0;

                    if (length < 0) length = bytesLength;

                    length = Math.Min(length, bytesLength - offset);
                    if (length > -1)
                    {
                        if (length == 0)
                            return String.Empty;
                        return UTF8.GetString(bytes, offset, length);
                    }
                }
            }
            return null;
        }

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
            Buffer.BlockCopy(ticks, 0, result, 0, ticks.Length);
            
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

        internal static short ToShort(this byte[] value, int offset)
        {
            return BitConverter.ToInt16(value, offset);
        }

        internal static int ToInt(this byte[] value, int offset)
        {
            return BitConverter.ToInt32(value, offset);
        }

        internal static long ToLong(this byte[] value, int offset)
        {
            return BitConverter.ToInt64(value, offset);
        }

        internal static ulong ToULong(this byte[] value, int offset)
        {
            return BitConverter.ToUInt64(value, offset);
        }

        internal static ushort ToUShort(this byte[] value, int offset)
        {
            return BitConverter.ToUInt16(value, offset);
        }

        internal static uint ToUInt(this byte[] value, int offset)
        {
            return BitConverter.ToUInt32(value, offset);
        }

        internal static decimal ToDecimal(this byte[] value, int offset)
        {
            var dbl = BitConverter.ToDouble(value, offset);
            return Convert.ToDecimal(dbl);
        }

        internal static double ToDouble(this byte[] value, int offset)
        {
            return BitConverter.ToDouble(value, offset);
        }

        internal static float ToFloat(this byte[] value, int offset)
        {
            return BitConverter.ToSingle(value, offset);
        }

        internal static DateTime? ToDateTime(this byte[] value, int offset)
        {
            var ticks = BitConverter.ToInt64(value, offset);
            if (value.Length >= offset + 9)
            {
                var kind = (DateTimeKind)value[offset + 8];
                return new DateTime(ticks, kind);
            }
            return new DateTime(ticks);
        }

        internal static char ToChar(this byte[] value, int offset)
        {
            return BitConverter.ToChar(value, offset);
        }

        #endregion FromBytes

        #endregion Bytes
    }
}
