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
using System.Diagnostics;
using System.Threading;

using Sweet.Actors.Rpc;

namespace Sweet.Actors.RpcLocalSystemTest
{
    class Program
    {
        private static int counter;
        private const int loop = 20000;

        private static void InitSystem(int port)
        {
            var serverOptions = (new RpcServerOptions())
                 .UsingIPAddress("127.0.0.1")
                 .UsingPort(port);

            var manager = new RpcManager(serverOptions);
            manager.Start();

            var systemOptions = ActorSystemOptions
                .UsingName("system-2")
                .UsingErrorHandler(
                    (actorSys, error) => { Console.WriteLine(error); },
                    (actorSys, process, msg, error) => { Console.WriteLine(error); });

            var actorSystem = ActorSystem.GetOrAdd(systemOptions);

            manager.Bind(actorSystem);

            var remoteActorOptions = ActorOptions
                .UsingName("system-1-actor-1")
                .UsingRemoteActorSystem("system-1")
                .UsingRemoteEndPoint("127.0.0.1", 17777);

            actorSystem.FromRemote(remoteActorOptions);
        }

        static void Call()
        {
            ActorSystem.TryGet("system-2", out ActorSystem actorSystem);
            actorSystem.TryGetRemote(new Aid("system-1", "system-1-actor-1"), out Pid remotePid);

            var sw = new Stopwatch();
            sw.Restart();

            /* for (var i = 0; i < loop; i++)
                remotePid.Tell("hello (fire & forget) - " + i.ToString("000000")); */

            /* var task = remotePid.Request("hello (do not forget)");
            task.ContinueWith((previousTask) => {
                IFutureResponse response = null;
                if (!(previousTask.IsCanceled || previousTask.IsFaulted))
                    response = previousTask.Result;

                Console.WriteLine(response?.Data ?? "(null response)");
            }); */

            /* sw.Stop();
            Console.WriteLine("Ellapsed time (ms): " + sw.ElapsedMilliseconds); */

            for (var i = 0; i < loop; i++)
            {
                remotePid.Request("hello (fire & forget) - " + i.ToString("000000")).ContinueWith(
                    (previousTask) => {
                    var count = Interlocked.Increment(ref counter);

                    if (count == 1)
                        sw.Restart();
                    else
                    {
                        if (count % 1000 == 0)
                            Console.WriteLine("Actor: " + count);

                        if (count == loop)
                        {
                            Interlocked.Exchange(ref counter, 0);

                            sw.Stop();
                            Console.WriteLine("Ellapsed time: " + sw.ElapsedMilliseconds);
                            Console.WriteLine("Concurrency: " + (loop * 1000 / sw.ElapsedMilliseconds) + " call per sec");
                        }
                    }
                });
            }
        }

        static void Main(string[] args)
        {
            InitSystem(18888);

            Console.WriteLine("Press ESC to exit, any key to continue ...");

            do
            {
                Console.Clear();
                Console.WriteLine("Press ESC to exit, any key to continue ...");

                Call();
            } while (ReadKey() != ConsoleKey.Escape);
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

                prevKey = buffer[len-1];
                if (len < bufferLen)
                    break;
            }

            return prevKey > -1 ? (ConsoleKey)prevKey : 0;
        }
    }
}
