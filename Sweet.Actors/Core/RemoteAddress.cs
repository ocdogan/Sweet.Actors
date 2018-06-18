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

namespace Sweet.Actors
{
    public class RemoteAddress
    {
        private int _hashCode;
        private Aid _actor;
        private RemoteEndPoint _endPoint;

        internal RemoteAddress(string host, int port, string actorSystem, string actor)
        {
            host = host?.Trim() ?? String.Empty;
            if (host == null)
                throw new ArgumentNullException(nameof(host));

            _endPoint = new RemoteEndPoint(host, port);
            _actor = new Aid(actorSystem, actor);
        }

        internal RemoteAddress(string host, int port, Aid actorId)
        {
            host = host?.Trim() ?? String.Empty;
            if (host == null)
                throw new ArgumentNullException(nameof(host));

            _endPoint = new RemoteEndPoint(host, port);
            _actor = actorId ?? throw new ArgumentNullException(nameof(actorId));
        }

        internal RemoteAddress(RemoteEndPoint endPoint, string actorSystem, string actor)
        {
            _endPoint = endPoint ?? throw new ArgumentNullException(nameof(endPoint));
            _actor = new Aid(actorSystem, actor);
        }

        internal RemoteAddress(RemoteEndPoint endPoint, Aid actorId)
        {
            _endPoint = endPoint ?? throw new ArgumentNullException(nameof(endPoint));
            _actor = actorId ?? throw new ArgumentNullException(nameof(actorId));
        }

        public RemoteEndPoint EndPoint => _endPoint;

        public Aid Actor => _actor;
        
        public override string ToString()
        {
            return $"actors://{_endPoint.Host}:{_endPoint.Port}/{_actor.ActorSystem}/{_actor.Actor}";
        } 

        public override int GetHashCode()
        {
            if (_hashCode == 0)
            {
                var hash = _endPoint.GetHashCode();
                _hashCode = 31 * hash + _actor.GetHashCode();
            }
            return _hashCode;
        }

        public override bool Equals(object obj)
        {
            if (obj == null)
                return false;
            
            if (obj is RemoteAddress ra)
                return (ra.GetHashCode() == GetHashCode()) &&
                    ra._endPoint == _endPoint &&
                    ra._actor == _actor;
            return false;
        }
    }
}