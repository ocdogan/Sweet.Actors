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
using System.Threading;
using System.Threading.Tasks;

namespace Sweet.Actors
{
    internal static class Common
    {
		private static readonly Action<Task> IgnoreTaskContinuation = t => { var ignored = t.Exception; };

		public static readonly int ProcessId = Environment.TickCount;

        private static bool? s_IsWinPlatform;
        private static bool? s_IsLinuxPlatform;

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

        public static bool CompareAndSet(ref int value, bool expectedValue, bool newValue)
        {
            var @new = newValue ? Constants.True : Constants.False;
            var expected = expectedValue ? Constants.True : Constants.False;

            return Interlocked.CompareExchange(ref value, @new, expected) == expected;
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

        internal static bool IsEmpty(this ExtEndPoint endPoint)
        {
            return (ReferenceEquals(endPoint, null) || endPoint.IsEmpty);
        }

		internal static void Ignore(this Task task)
        {
            if (task.IsCompleted)
            {
                var ignored = task.Exception;
            }
            else
            {
                task.ContinueWith(
                    IgnoreTaskContinuation,
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
        }
    }
}
