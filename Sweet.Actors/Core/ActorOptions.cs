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
using System.Net;

namespace Sweet.Actors
{
    public sealed class ActorOptions : OptionsBase<ActorOptions>
    {
        public static readonly ActorOptions Default = new ActorOptions(null);

        private IActor _actor;
        private Type _actorType;
        private string _remoteActorSystem;
        private RemoteEndPoint _endPoint;
        private IDictionary<string, object> _initialContextData;

        private ActorOptions(string name)
            : base(name)
        { }

        public static ActorOptions UsingName(string name)
        {
            return new ActorOptions(name);
        }

        public ActorOptions UsingInitialContextData(IDictionary<string, object> initialContextData)
        {
            _initialContextData = initialContextData;
            return this;
        }

        public ActorOptions UsingRemoteActorSystem(string remoteActorSystem)
        {
            _remoteActorSystem = remoteActorSystem?.Trim();
            return this;
        }

        public ActorOptions UsingRemoteEndPoint(string host, int port)
        {
            _endPoint = new RemoteEndPoint(host, port);
            return this;
        }

        public ActorOptions UsingRemoteEndPoint(RemoteEndPoint endPoint)
        {
            _endPoint = endPoint;
            return this;
        }

        public ActorOptions UsingRemoteEndPoint(IPEndPoint endPoint)
        {
            _endPoint = (endPoint == null) ? null : 
                new RemoteEndPoint(endPoint.Address.ToString(), endPoint.Port);
            return this;
        }

        public ActorOptions UsingActor(IActor actor)
        {
            _actor = actor;
            return this;
        }

        public ActorOptions UsingActor<T>()
            where T : class, IActor, new()
        {
            _actorType = typeof(T);
            return this;
        }

        public IActor Actor => _actor;

        public Type ActorType => _actorType;

        public string RemoteActorSystem => _remoteActorSystem;

        public RemoteEndPoint EndPoint => _endPoint;

        public IDictionary<string, object> InitialContextData => _initialContextData;
    }
}
