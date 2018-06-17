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
    public class RemoteEndPoint
    {
        public static readonly RemoteEndPoint Default = new RemoteEndPoint(null, -1);
          
        private int _port;
        private string _host;
        private int _hashCode;

        public RemoteEndPoint(string host, int port)
        {
            _host = host?.Trim();
            if (string.IsNullOrEmpty(host))
                _host = Constants.DefaultHost;
            _port = port < 0 ? Constants.DefaultPort : port;
        }

        public string Host => _host;
        public int Port => _port;

        public override string ToString()
        {
            return $"{Host}:{Port}";
        } 

        public override int GetHashCode()
        {
            if (_hashCode == 0)
            {
                var hash = 1 + (Host ?? String.Empty).GetHashCode();
                _hashCode = 31 * hash + Port.GetHashCode();
            }
            return _hashCode;
        }

        public override bool Equals(object obj)
        {
            if (obj == null)
                return false;
            
            if (obj is RemoteEndPoint rr)
                return (rr.GetHashCode() == GetHashCode()) &&
                    rr.Port == Port &&
                    rr.Host == Host;
            return false;
        }
    }
}