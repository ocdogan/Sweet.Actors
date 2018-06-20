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
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Sweet.Actors;

namespace Sweet.Actors.ConsoleTest
{
    class Program
    {
        private const int loop = 1000000;
        private static ManualResetEvent resetEvent = new ManualResetEvent(false);

        class DummyActor : IActor
        {
            private int _state;
            private static readonly Task Completed = Task.FromResult(0);

            public Task OnReceive(IContext ctx, IMessage message)
            {
                var i = Interlocked.Add(ref _state, 1);
                if (message is IFutureMessage futureMessage)
                {
                    ctx.RespondTo(futureMessage, i);
                }

                if (i >= loop)
                {
                    Interlocked.Exchange(ref _state, 0);

                    resetEvent.Set();
                    Console.WriteLine("Completed");
                }
                return Completed;
            }
        }

        static void Main(string[] args)
        {
            // CounterTest();
            // AverageTest();

            // ChunkedStreamTest();
            // ClientServerTest();
            RemoteTest();

            // ActorTest();
        }

        private static void RunRemoteSystem()
        {
            var remoteServerOptions = (new RpcServerOptions())
                .UsingIPAddress("127.0.0.1")
                .UsingPort(17777);

            var remoteManager = new RpcManager(remoteServerOptions);
            remoteManager.Start();

            var remoteSystemOptions = ActorSystemOptions
                .UsingName("remote-system")
                .UsingErrorHandler((process, msg, error) => { Console.WriteLine(error); });

            var remoteSystem = ActorSystem.GetOrAdd(remoteSystemOptions);
            remoteManager.Bind(remoteSystem);

            var actorOptions = ActorOptions
                .UsingName("remote-actor");

            var Completed = Task.FromResult(0);

            remoteSystem.FromFunction((ctx, message) => {
                Console.WriteLine(message.Data ?? "(null request)");

                if (message.MessageType == MessageType.FutureMessage)
                    ctx.RespondTo(message, "world");

                return Completed;
            },
            actorOptions);
        }

        private static void RunLocalSystem()
        {
            var localServerOptions = (new RpcServerOptions())
                .UsingIPAddress("127.0.0.1")
                .UsingPort(18888);

            var localManager = new RpcManager(localServerOptions);
            localManager.Start();

            var localSystemOptions = ActorSystemOptions
                .UsingName("local-system")
                .UsingErrorHandler((process, msg, error) => { Console.WriteLine(error); });

            var localSystem = ActorSystem.GetOrAdd(localSystemOptions);

            localManager.Bind(localSystem);

            var remoteActorOptions = ActorOptions
                .UsingName("remote-actor")
                .UsingRemoteActorSystem("remote-system")
                .UsingRemoteEndPoint("127.0.0.1", 17777);

            var remotePid = localSystem.FromRemote(remoteActorOptions);
            // remotePid.Tell("hello (fire & forget)");

            var task = remotePid.Request("hello (do not forget)");
            task.ContinueWith((t) => {
                var response = t.Result;
                Console.WriteLine(response?.Data ?? "(null response)");
            });
        }

        private static void RemoteTest()
        {
            RunRemoteSystem();
            RunLocalSystem();

            Console.WriteLine("Press any key to exit");
            Console.Read();

            /* var remoteManager = new RpcManager((new RpcServerOptions()).UsingIPAddress("127.0.0.1"));
            remoteManager.Start();

            var systemOptions = ActorSystemOptions
                .UsingName("system")
                .UsingErrorHandler((process, msg, error) => { Console.WriteLine(error); });

            var actorSystem = ActorSystem.GetOrAdd(systemOptions);

            remoteManager.Bind(actorSystem);

            Thread.Sleep(2000);

            Console.WriteLine(remoteManager.EndPoint);

            var actorOptions = ActorOptions
                .UsingName("actor")
                .UsingSequentialInvokeLimit(1000)
                .UsingRemoteActorSystem("system")
                .UsingRemoteEndPoint(remoteManager.EndPoint);

            var remotePid = actorSystem.FromRemote(actorOptions);
            remotePid.Tell("hello");

            Console.WriteLine("Press any key to exit");
            Console.Read();
            */
        }

        private static void ChunkedStreamTest()
        {
            var buffer = new byte[100];

            var data1 = Encoding.UTF8.GetBytes("client.Send(Message.Empty, Address.Unknown);");
            var data2 = Encoding.UTF8.GetBytes("xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx");

            using (var stream = new ChunkedStream())
            {
                var loop = 10000;
                for (var i = 0; i < loop; i++)
                    stream.Write(data1, 0, data1.Length);

                var dataSize = loop * data1.Length;
                Console.WriteLine("Expected size: " + dataSize + ", stream size: " + stream.Length);

                using (var reader = stream.NewReader())
                {
                    reader.Position = 0;

                    int readLen;
                    var readSize = 0;

                    while ((readLen = reader.Read(buffer, 0, buffer.Length)) > 0)
                        readSize += readLen;

                    Console.WriteLine("Expected size: " + dataSize + ", read size: " + readSize);

                    var trimBy = 10;

                    dataSize = (loop - trimBy) * data1.Length;
                    stream.TrimLeft(trimBy * data1.Length);

                    Console.WriteLine("Expected size: " + dataSize + ", stream size: " + stream.Length);

                    for (var i = 0; i < trimBy; i++)
                        stream.Write(data2, 0, data2.Length);

                    dataSize = loop * data1.Length;
                    Console.WriteLine("Expected size: " + dataSize + ", stream size: " + stream.Length);

                    reader.Position = (loop - 2 * trimBy) * data1.Length;

                    var dataBuffer = new byte[data1.Length];
                    while ((readLen = reader.Read(dataBuffer, 0, dataBuffer.Length)) > 0)
                        Console.WriteLine(Encoding.UTF8.GetString(dataBuffer, 0, readLen));
                }
            }

            while (true)
                Thread.Sleep(1000);
        }

        private static void ActorTest()
        {
            var sw = new Stopwatch();

            var systemOptions = ActorSystemOptions
                .UsingName("default")
                .UsingErrorHandler((process, msg, error) => { Console.WriteLine(error); });

            var actorOptions = ActorOptions
                .UsingName("dummy")
                .UsingSequentialInvokeLimit(1000);

            var actorSystem = ActorSystem.GetOrAdd(systemOptions);
            var actorPid = actorSystem.FromType<DummyActor>(actorOptions);

            var Completed = Task.FromResult(0);

            var state = 0;
            var functionPid = actorSystem.FromFunction((ctx, msg) =>
            {
                var i = Interlocked.Add(ref state, 1);
                if (msg is IFutureMessage futureMessage)
                    ctx.RespondTo(futureMessage, i);

                if (i >= loop)
                {
                    Interlocked.Exchange(ref state, 0);

                    resetEvent.Set();
                    Console.WriteLine("Completed");
                }
                return Completed;
            });

            do
            {
                var t = actorPid.Request<int>("hello");
                t.ContinueWith((ca) =>
                {
                    if (ca.IsCompleted)
                    {
                        var r = ca.Result;
                        if (r is IFutureError fe)
                            Console.WriteLine(fe.Exception);
                        else
                            Console.WriteLine(r.Data);
                    }
                });

                actorPid.Tell(message: "hello", timeoutMSec: 1);

                resetEvent.Reset();

                sw.Restart();
                for (var i = 0; i < loop; i++)
                    actorPid.Tell("hello");

                resetEvent.WaitOne();
                sw.Stop();

                Console.WriteLine("Ellapsed, from actor: " + sw.ElapsedMilliseconds);

                resetEvent.Reset();

                sw.Restart();
                for (var i = 0; i < loop; i++)
                    functionPid.Tell(i);

                resetEvent.WaitOne();
                sw.Stop();

                Console.WriteLine("Ellapsed, from function: " + sw.ElapsedMilliseconds);
            }
            while (Console.ReadKey().Key != ConsoleKey.Escape);
        }

        private static void CounterTest()
        {
            var rnd = new Random();
            var counter = new MetricsCounter(1);

            for (var i = 0; i < 1000; i++)
            {
                Console.WriteLine(counter.Tick());
                Thread.Sleep(20);

                if (rnd.Next(0, 1) != 0)
                    Thread.Sleep(rnd.Next(1, 5) * 1000);
            }
        }

        private static void AverageTest()
        {
            var items = new int[] { 1, 3, 5, 12 };

            var avg = new MetricsAverage();
            for (var i = 0; i < items.Length; i++)
                Console.WriteLine(avg.Tick(items[i]));
        }
    }
}
