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
using System.Collections.Concurrent;
using System.Text;

namespace Sweet.Actors.Rpc
{
    public static class RpcSerializerRegistry
    {
        private class Registry
        {
            public Type SerializerType;
            public IWireSerializer Instance;
        }

        private static readonly ConcurrentDictionary<string, Registry> _serializerRegistry =
            new ConcurrentDictionary<string, Registry>();

        private static void ValidateRegistryName(string registryName)
        {
            var len = registryName?.Length ?? 0;
            if (len == 0)
                throw new ArgumentNullException(nameof(registryName));

            len = Encoding.UTF8.GetByteCount(registryName);
            if (len > RpcHeaderSizeOf.SerializerKey)
                throw new ArgumentOutOfRangeException(nameof(registryName));
        }

        public static IWireSerializer Get(string registryName)
        {
            ValidateRegistryName(registryName);

            if (!_serializerRegistry.TryGetValue(registryName, out Registry reg))
                return null;

            if (reg.Instance == null)
            {
                lock (reg)
                {
                    if (reg.Instance == null)
                        reg.Instance = (IWireSerializer)Activator.CreateInstance(reg.SerializerType);
                }
            }
            return reg.Instance;
        }

        public static void Register<T>(string registryName)
            where T : class, IWireSerializer, new()
        {
            ValidateRegistryName(registryName);
            _serializerRegistry.GetOrAdd(registryName, NewRegistry<T>);
        }

        private static Registry NewRegistry<T>(string registryName) 
            where T : class, IWireSerializer, new()
        {
            return new Registry { SerializerType = typeof(T) };
        }
    }
}