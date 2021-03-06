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
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

using Sweet.Actors;
using Sweet.Actors.Rpc;

namespace Sweet.Actors.RpcLocalSystemTest
{
    class Program
    {
        private static int counter;
        private const int loop = 200000;

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
                    (actorSys, process, msg, error) => {
                        Console.WriteLine(error);
                    });

            var actorSystem = ActorSystem.GetOrAdd(systemOptions);

            manager.Bind(actorSystem);

            var remoteActorOptions = ActorOptions
                .UsingName("system-1-actor-1")
                .UsingRemoteActorSystem("system-1")
                .UsingRemoteEndPoint("127.0.0.1", 17777);

            actorSystem.FromRemote(remoteActorOptions);
        }

        class DummyConnection : IRpcConnection
        {
            public Socket Connection => null;

            public IPEndPoint RemoteEndPoint => throw new NotImplementedException();

            public object State => null;

            public void Flush()
            { }

            public void Send(WireMessage message)
            { }

            public void Send(WireMessage[] messages)
            { }
        }

        private class DummyMessageWriter : RpcMessageWriter
        {
            private Stream _stream;

            public DummyMessageWriter(Stream stream, IRpcConnection conn, string serializerKey)
                : base(conn, serializerKey)
            {
                _stream = stream;
            }

            protected override void Send(Socket socket, byte[] buffer, int bufferLen)
            {
                _stream.Write(buffer, 0, bufferLen);
            }
        }

        static void Call()
        {
            ActorSystem.TryGet("system-2", out ActorSystem actorSystem);
            actorSystem.TryGetRemote(new Aid("system-1", "system-1-actor-1"), out Pid remotePid);

            var sw = new Stopwatch();
            sw.Restart();

            // for (var i = 0; i < loop; i++)
            //     remotePid.Tell("hello (fire & forget) - " + i.ToString("000000"));

            /* var task = remotePid.Request("hello (do not forget)");
            task.ContinueWith((previousTask) => {
                IFutureResponse response = null;
                if (!(previousTask.IsCanceled || previousTask.IsFaulted))
                    response = previousTask.Result;

                Console.WriteLine(response?.Data ?? "(null response)");
            }); */

            /* using (var stream = new ChunkedStream())
            {
                var writer = new DummyMessageWriter(stream, new DummyConnection(), "Default");

                const int cycle = 500;
                const int bulkSize = 500;

                var tresh = " - " + new string('x', 1000);

                var rnd = new Random();

                var total = 0;
                var sendCount = cycle * bulkSize;

                var sentList = new List<int>(sendCount);

                while (sendCount > 0)
                {
                    var cnt = rnd.Next(1, bulkSize);
                    sendCount -= cnt;

                    var list = new List<WireMessage>(cnt);
                    for (var j = 0; j < cnt; j++)
                    {
                        var message = new WireMessage
                        {
                            State = WireMessageState.Default,
                            MessageType = MessageType.Default,
                            Id = WireMessageId.Next(),
                            Data = "hello (fire & forget) - " + (total++).ToString("000000") + tresh,
                            From = Aid.Unknown,
                            To = Aid.Unknown,
                        };

                        list.Add(message);
                    }

                    writer.Write(list.ToArray());
                }

                sw.Stop();
                Console.WriteLine("Ellapsed time (ms): " + sw.ElapsedMilliseconds);

                sw.Restart();

                var count = 0;
                stream.Position = 0;

                while (RpcMessageParser.TryParse(stream, out IEnumerable<WireMessage> messages))
                {
                    foreach (var msg in messages)
                        count++;
                }

                if (count != total)
                    Console.WriteLine("error");
            } */

            sw.Stop();
            Console.WriteLine("Ellapsed time (ms): " + sw.ElapsedMilliseconds);

            for (var i = 0; i < loop; i++)
            {
                remotePid.Request("hello (fire & forget) - " + i.ToString("000000")).ContinueWith(
                    (previousTask) =>
                    {
                        var count = Interlocked.Increment(ref counter);

                        if (count == 1)
                            sw.Restart();

                        if (count % 1000 == 0)
                            Console.WriteLine("Actor: " + count);

                        if (count == loop)
                        {
                            Interlocked.Exchange(ref counter, 0);

                            sw.Stop();
                            Console.WriteLine("Ellapsed time: " + sw.ElapsedMilliseconds);
                            Console.WriteLine("Concurrency: " + (loop * 1000 / sw.ElapsedMilliseconds) + " call per sec");
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
