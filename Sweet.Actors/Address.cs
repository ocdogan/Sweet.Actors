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
    public sealed class Address
    {
        internal Address(string host, int port, string id)
        {
            Port = Math.Max(0, port);
            Host = (host ?? String.Empty).Trim();
            Id = (id ?? String.Empty).Trim();
        }

        public static Address Unknown => new Address(String.Empty, 0, String.Empty);

        public string Host { get; }

        public int Port { get; }

        public string Id { get; }

        public bool IsUnknown()
        {
            return ReferenceEquals(this, Unknown) || (
                Host == String.Empty
                && Port == 0
                && String.IsNullOrEmpty(Id)
            );
        }

        public Address Clone()
        {
            if (ReferenceEquals(this, Unknown))
                return this;
            return new Address(Host, Port, Id);
        }

        public override string ToString()
        {
            return String.Format(Constants.AddressFormat, Host, Port, Id);
        }

        public static Address Parse(string str)
        {
            str = (str ?? String.Empty).Trim();
            if (!String.IsNullOrEmpty(str))
            {
                var len = str.Length;
                if (len >= Constants.EmptyProtocolLength &&
                    str.StartsWith(Constants.Protocol, StringComparison.OrdinalIgnoreCase))
                {
                    var parts = str.Substring(Constants.ProtocolLength).Split('/');

                    if ((parts != null) && (parts.Length > 1))
                    {
                        var location = String.Empty;
                        var id = String.Empty;

                        var areaCode = 0;

                        var pos = parts[0].IndexOf(':');
                        if (pos == -1)
                            throw new Exception(Errors.InvalidAddress);

                        location = (parts[0].Substring(0, pos) ?? String.Empty).Trim();

                        var sAreaCode = (parts[0].Substring(pos + 1) ?? String.Empty).Trim();
                        if (!int.TryParse(sAreaCode, out areaCode) || areaCode < 0)
                            throw new Exception(Errors.InvalidAddress);

                        id = (parts[1] ?? String.Empty).Trim();
                        if (String.IsNullOrEmpty(id))
                            throw new Exception(Errors.InvalidAddress);

                        return new Address(location, areaCode, id);
                    }
                }
            }
            throw new Exception(Errors.InvalidAddress);
        }
    }
}
