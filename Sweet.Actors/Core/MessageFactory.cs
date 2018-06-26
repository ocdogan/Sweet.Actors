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
using System.Collections.Generic;
using System.Reflection;

namespace Sweet.Actors
{
    public static class MessageFactory
    {
        private static readonly ConcurrentDictionary<Type, Type> _futureErrorRepository = new ConcurrentDictionary<Type, Type>();
        private static readonly ConcurrentDictionary<Type, Type> _futureMessageRepository = new ConcurrentDictionary<Type, Type>();
        private static readonly ConcurrentDictionary<Type, Type> _futureResponseRepository = new ConcurrentDictionary<Type, Type>();

        public static IFutureMessage CreateFutureMessage(Type responseType, object data, 
                               Aid from = null, IDictionary<string, string> header = null, int timeoutMSec = 0)
        {
            var repositoryType = _futureMessageRepository.GetOrAdd(responseType, 
                (elementType) => {
                    return typeof(FutureMessage<>).MakeGenericType(elementType);
                });

            return (IFutureMessage)Activator.CreateInstance(repositoryType, new object[] {
                data, new TaskCompletor<IFutureResponse>(timeoutMSec), from, header
            });
        }

        public static IFutureResponse CreateFutureResponse(Type responseType, object data,
                               Aid from = null, IDictionary<string, string> header = null)
        {
            var repositoryType = _futureResponseRepository.GetOrAdd(responseType, 
                (elementType) => {
                    return typeof(FutureResponse<>).MakeGenericType(elementType);
                });
            return (IFutureResponse)Activator.CreateInstance(repositoryType, new object[] { data, from, header });
        }

        public static IFutureError CreateFutureError(Type responseType, Exception error,
                               Aid from = null, IDictionary<string, string> header = null)
        {
            var repositoryType = _futureErrorRepository.GetOrAdd(responseType, 
                (elementType) => {
                    return typeof(FutureError<>).MakeGenericType(elementType);
                });
            return (IFutureError)Activator.CreateInstance(repositoryType, new object[] { error, from, header });
        }    
    }
}