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
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

using Sweet.Actors.Rpc;

namespace Sweet.Actors.RpcRemoteSystemTest
{
    class Program
    {
        private static int counter;
        private const int loop = 200000;

        private static void RunRemoteSystem(int port)
        {
            var serverOptions = (new RpcServerOptions())
                .UsingIPAddress("127.0.0.1")
                .UsingPort(port);

            var manager = new RpcManager(serverOptions);
            manager.Start();

            var systemOptions = ActorSystemOptions
                .UsingName("system-1")
                .UsingErrorHandler(
                    (actorSys, error) => { Console.WriteLine(error); },
                    (actorSys, process, msg, error) => { Console.WriteLine(error); });

            var actorSystem = ActorSystem.GetOrAdd(systemOptions);
            manager.Bind(actorSystem);

            var actorOptions = ActorOptions
                .UsingName("system-1-actor-1");

            var Completed = Task.FromResult(0);

            var sw = new Stopwatch();

            var pid = actorSystem.FromFunction((ctx, message) => {
                var count = Interlocked.Increment(ref counter);

                if (count == 1)
                    sw.Restart();

                if (count % 1000 == 0)
                    Console.WriteLine("Actor: " + count);

                if (count == loop)
                {
                    Interlocked.Exchange(ref counter, 0);

                    sw.Stop();

                    var elapsed = sw.ElapsedMilliseconds;
                    if (elapsed <= 0)
                    {
                        Console.WriteLine("Ellapsed time: 0");
                        Console.WriteLine("Concurrency: " + loop + " call per sec");
                    }
                    else
                    {
                        Console.WriteLine("Ellapsed time: " + elapsed);
                        Console.WriteLine("Concurrency: " + (loop * 1000 / elapsed) + " call per sec");
                    }
                }

                if (message.MessageType == MessageType.FutureMessage)
                {
                    if (count == loop)
                        Console.WriteLine("All complete");
                    ctx.RespondTo(message, "world " + count.ToString("000"));
                }

                return Completed;
            },
            actorOptions);
        }

        static void Main(string[] args)
        {
            RunRemoteSystem(17777);
            do
            {
                Console.Clear();
                Console.WriteLine("Press any key to exit");
            }
            while (ReadKey() != ConsoleKey.Escape);
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
