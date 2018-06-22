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

namespace Sweet.Actors.RpcTestServer2
{
    class Program
    {
        private const int loop = 20000;

        private static void RunRemoteSystem(int port)
        {
            var serverOptions = (new RpcServerOptions())
                 .UsingIPAddress("127.0.0.1")
                 .UsingPort(port);

            var manager = new RpcManager(serverOptions);
            manager.Start();

            var systemOptions = ActorSystemOptions
                .UsingName("system-2")
                .UsingErrorHandler((process, msg, error) => { Console.WriteLine(error); });

            var actorSystem = ActorSystem.GetOrAdd(systemOptions);

            manager.Bind(actorSystem);

            var remoteActorOptions = ActorOptions
                .UsingName("system-1-actor-1")
                .UsingRemoteActorSystem("system-1")
                .UsingRemoteEndPoint("127.0.0.1", 17777);

            var remotePid = actorSystem.FromRemote(remoteActorOptions);
            for (var i = 0; i < loop; i++)
                remotePid.Tell("hello (fire & forget) - " + i.ToString("0000"));

            /* var task = remotePid.Request("hello (do not forget)");
            task.ContinueWith((previousTask) => {
                var response = previousTask.Result;
                Console.WriteLine(response?.Data ?? "(null response)");
            }); */
        }

        static void Main(string[] args)
        {
            RunRemoteSystem(18888);

            Console.WriteLine("Press any key to exit");
            Console.Read();
        }
    }
}
