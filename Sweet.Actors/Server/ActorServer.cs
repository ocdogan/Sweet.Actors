using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Sweet.Actors
{
    public class ActorServer : Disposable
    {
        public ActorServer(ServerEndPoint endPoint)
        {
            EndPoint = endPoint ?? throw new ArgumentNullException(nameof(endPoint));
        }

        public ServerEndPoint EndPoint { get; }


    }
}
