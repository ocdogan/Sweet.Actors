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
using System.Collections.Generic;
using System.Diagnostics;

using Sweet.Actors;
using Sweet.Actors.Rpc;

namespace Sweet.Actors.SerializeTest
{
    class Program
    {
        private const int loop = 100000;

        static void Main(string[] args)
        {
            SerializeTest4();
        }

        static void SerializeTest1()
        {
            var sw = new Stopwatch();

            Console.WriteLine("Press ESC to exit, any key to continue ...");

            while (ReadKey() != ConsoleKey.Escape)
            {
                Console.Clear();
                Console.WriteLine("Press ESC to exit, any key to continue ...");

                sw.Restart();

                for (var i = 0; i < loop; i++)
                {
                    var message = new WireMessage
                    {
                        Data = "hello (fire & forget) - " + i.ToString("000000"),
                        MessageType = MessageType.Default
                    };
                }

                sw.Stop();

                Console.WriteLine("Ellapsed time (ms): " + sw.ElapsedMilliseconds);
                Console.WriteLine("Concurrency: " + (loop * 1000 / sw.ElapsedMilliseconds) + " call per sec");
            }
        }

        static void SerializeTest2()
        {
            var serializer = new DefaultRpcSerializer();

            var sw = new Stopwatch();

            Console.WriteLine("Press ESC to exit, any key to continue ...");

            while (ReadKey() != ConsoleKey.Escape)
            {
                Console.Clear();
                Console.WriteLine("Press ESC to exit, any key to continue ...");

                var message = new WireMessage
                {
                    Data = "hello (fire & forget) - 000000",
                    MessageType = MessageType.Default
                };

                sw.Restart();

                for (var i = 0; i < loop; i++)
                    serializer.Serialize(new WireMessage[] { message });

                sw.Stop();

                Console.WriteLine("Ellapsed time (ms): " + sw.ElapsedMilliseconds);
                Console.WriteLine("Concurrency: " + (loop * 1000 / sw.ElapsedMilliseconds) + " call per sec");
            }
        }

        static void SerializeTest3()
        {
            var serializer = new DefaultRpcSerializer();

            var sw = new Stopwatch();

            Console.WriteLine("Press ESC to exit, any key to continue ...");

            while (ReadKey() != ConsoleKey.Escape)
            {
                Console.Clear();
                Console.WriteLine("Press ESC to exit, any key to continue ...");

                sw.Restart();

                for (var i = 0; i < loop; i++)
                {
                    var message = new WireMessage
                    {
                        Data = "hello (fire & forget) - " + i.ToString("000000"),
                        MessageType = MessageType.Default
                    };

                    serializer.Serialize(new WireMessage[] { message });
                }

                sw.Stop();

                Console.WriteLine("Ellapsed time (ms): " + sw.ElapsedMilliseconds);
                Console.WriteLine("Concurrency: " + (loop * 1000 / sw.ElapsedMilliseconds) + " call per sec");
            }
        }

        static void SerializeTest4()
        {
            var serializer = new DefaultRpcSerializer();

            var sw = new Stopwatch();

            Console.WriteLine("Press ESC to exit, any key to continue ...");

            while (ReadKey() != ConsoleKey.Escape)
            {
                Console.Clear();
                Console.WriteLine("Press ESC to exit, any key to continue ...");

                sw.Restart();
                const int BulkSize = 100;

                for (var i = 0; i < loop; i++)
                {
                    var list = new List<WireMessage>(BulkSize / 2);

                    list.Add(new WireMessage
                    {
                        Data = "hello (fire & forget) - " + i.ToString("000000"),
                        MessageType = MessageType.Default
                    });

                    serializer.Serialize(list.ToArray());
                }

                sw.Stop();

                Console.WriteLine("Ellapsed time (ms): " + sw.ElapsedMilliseconds);
                Console.WriteLine("Concurrency: " + (loop * 1000 / sw.ElapsedMilliseconds) + " call per sec");
            }
        }

        private static bool IsWinPlatform
        {
            get
            {
                var pid = Environment.OSVersion.Platform;
                switch (pid)
                {
                    case PlatformID.Win32NT:
                    case PlatformID.Win32S:
                    case PlatformID.Win32Windows:
                    case PlatformID.WinCE:
                        return true;
                    default:
                        return false;
                }
            }
        }

        private static ConsoleKey ReadKey()
        {
            if (IsWinPlatform || !Console.IsInputRedirected)
                return Console.ReadKey(true).Key;

            var prevKey = -1;

            var input = Console.In;

            const int bufferLen = 256;
            var buffer = new char[bufferLen];

            while (true)
            {
                var len = input.Read(buffer, 0, bufferLen);
                if (len < 1)
                {
                    if (prevKey > -1)
                        break;
                }

                prevKey = buffer[len - 1];
                if (len < bufferLen)
                    break;
            }

            return prevKey > -1 ? (ConsoleKey)prevKey : 0;
        }
    }
}
