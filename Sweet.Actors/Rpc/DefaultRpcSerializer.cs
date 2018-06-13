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

namespace Sweet.Actors
{
    public class DefaultRpcSerializer : IRpcSerializer
    {
        private Serializer _serializer = new Serializer(new SerializerOptions(versionTolerance: true, preserveObjectReferences: true));

        public (IMessage, Pid) Deserialize(byte[] data)
        {
            if (data == null || data.Length == 0)
                return (Message.Empty, Pid.Unknown);

            using (var stream = new ChunkedStream(data))
            {
                return (_serializer.Deserialize<RpcMessage>(stream)).ToActualMessage();
            }
        }

        public (IMessage, Pid) Deserialize(Stream stream)
        {
            if (stream == null)
                return (Message.Empty, Pid.Unknown);
            return (_serializer.Deserialize<RpcMessage>(stream)).ToActualMessage();
        }

        public byte[] Serialize(RpcMessage msg)
        {
            if (msg != null)
            {
                using (var ms = new ChunkedStream())
                {
                    _serializer.Serialize(msg, ms);
                    return ms.ToArray();
                }
            }
            return null;
        }
    }
}