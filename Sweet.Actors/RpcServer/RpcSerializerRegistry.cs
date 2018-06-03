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

namespace Sweet.Actors
{
    public static class RpcSerializerRegistry
    {
        private class Registry
        {
            public Type SerializerType;
            public IRpcSerializer Instance;
        }

        private static readonly ConcurrentDictionary<string, Registry> _registry =
            new ConcurrentDictionary<string, Registry>();

        public static IRpcSerializer Get(string type)
        {
            Registry reg;
            if (!_registry.TryGetValue(type, out reg))
                return null;

            if (reg.Instance == null)
            {
                lock (reg)
                {
                    if (reg.Instance == null)
                        reg.Instance = (IRpcSerializer)Activator.CreateInstance(reg.SerializerType);
                }
            }
            return reg.Instance;
        }

        public static void Register<T>(string type)
            where T : class, IRpcSerializer, new()
        {
            _registry.GetOrAdd(type, (t) => new Registry { SerializerType = typeof(T)});
        }
    }
}