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

            public Task OnReceive(IContext ctx, IMessage msg)
            {
                var i = Interlocked.Add(ref _state, 1);
                if (msg is IFutureMessage futureMessage)
                {
                    ((IFutureContext)ctx).RespondTo(futureMessage, i);
                }

                if (i >= loop)
                {
                    Interlocked.Exchange(ref _state, 0);

                    resetEvent.Set();
                    Console.WriteLine("Completed");
                }
                return Receive.Completed;
            }
        }

        static void Main(string[] args)
        {
            // CounterTest();
            // AverageTest();

            ServerTest();

            // ActorTest();
        }

        private static void ServerTest()
        {
            var serverSettings = (new RpcServerSettings()).UsingIPAddress("127.0.0.1");

            var server = new RpcServer(serverSettings);
            server.Start();

            Thread.Sleep(2000);

            var serverEP = server.EndPoint;
            var clientSettings = (new RpcClientSettings()).UsingEndPoint(serverEP);

            Console.WriteLine(serverEP);

            var client = new RpcClient(clientSettings);

            Task.Factory.StartNew(() => {
                client.Connect();
                // client.Send(Message.Empty, Address.Unknown);
            }, TaskCreationOptions.LongRunning);

            while (true)
                Thread.Sleep(1000);
        }

        private static void ActorTest()
        {
            var sw = new Stopwatch();

            var sysOptions = ActorSystemOptions
                .UsingName("default")
                .UsingErrorHandler((process, msg, error) => { Console.WriteLine(error); });

            var actorOptions = ActorOptions
                .UsingName("dummy")
                .UsingSequentialInvokeLimit(1000);

            var actorSystem = ActorSystem.GetOrAdd(sysOptions);
            var actorPid = actorSystem.FromType<DummyActor>(actorOptions);

            var state = 0;
            var functionPid = actorSystem.FromFunction((ctx, msg) =>
            {
                var i = Interlocked.Add(ref state, 1);
                if (msg is IFutureMessage futureMessage)
                {
                    ((IFutureContext)ctx).RespondTo(futureMessage, i);
                }

                if (i >= loop)
                {
                    Interlocked.Exchange(ref state, 0);

                    resetEvent.Set();
                    Console.WriteLine("Completed");
                }
                return Receive.Completed;
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
