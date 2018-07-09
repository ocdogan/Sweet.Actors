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
using System.IO;
using Wire;

namespace Sweet.Actors.Rpc
{
    public class DefaultRpcSerializer : IWireSerializer
    {
        private Serializer _serializer = new Serializer(new SerializerOptions(versionTolerance: true, preserveObjectReferences: true));

        public RemoteMessage Deserialize(byte[] data)
        {
            if (data == null || data.Length == 0)
                return null;

            using (var stream = new ChunkedStream(data))
            {
                return (_serializer.Deserialize<WireMessage>(stream)).ToRemoteMessage();
            }
        }

        public RemoteMessage Deserialize(Stream stream)
        {
            if (stream == null)
                return null;
            return (_serializer.Deserialize<WireMessage>(stream)).ToRemoteMessage();
        }

        public byte[] Serialize(WireMessage message)
        {
            if (message != null)
            {
                using (var stream = new ChunkedStream())
                { 
                    _serializer.Serialize(message, stream);
                    return stream.ToArray();
                }
            }
            return null;
        }

        public long Serialize(WireMessage message, Stream stream)
        {
            if (message != null && 
                stream != null && stream.CanWrite)
            {
                var previousPos = stream.Position;
                 _serializer.Serialize(message, stream);

                return Math.Max(-1L, stream.Position - previousPos);
            }
            return -1L;
        }
    }
}