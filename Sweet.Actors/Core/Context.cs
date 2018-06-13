﻿#region License
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
using System.Collections.Concurrent;

namespace Sweet.Actors
{
    public interface IContext
    {
        bool HasData(string key);
        object GetData(string key);
        void SetData(string key, object data);
        bool TryGetData(string key, out object data);
    }

    public interface IFutureContext : IContext
    {
        void RespondTo<T>(IFutureMessage future, T response, IDictionary<string, string> header = null);
        void RespondToWithError<T>(IFutureMessage future, Exception e, IDictionary<string, string> header = null);
    }

    internal class Context : Disposable, IContext, IFutureContext
    {
        private Pid _pid;
        private Process _process;

        private ConcurrentDictionary<string, object> _dataCtx = new ConcurrentDictionary<string, object>();

        internal Context(Process process)
        {
            _process = process;
            _pid = _process.Pid;
        }

        public Pid Pid => _pid;

        protected override void OnDispose(bool disposing)
        {
            _pid = null;
            _process = null;

            base.OnDispose(disposing);
        }

        public bool HasData(string key)
        {
            return _dataCtx.ContainsKey(key);
        }

        public object GetData(string key)
        {
            _dataCtx.TryGetValue(key, out object result);
            return result;
        }

        public bool TryGetData(string key, out object data)
        {
            return _dataCtx.TryGetValue(key, out data);
        }

        public void SetData(string key, object data)
        {
            _dataCtx[key] = data;
        }

        public void RespondTo<T>(IFutureMessage future, T response, IDictionary<string, string> header = null)
        {
            ((FutureMessage)future).Respond(response, _pid, header);
        }

        public void RespondToWithError<T>(IFutureMessage future, Exception e, IDictionary<string, string> header = null)
        {
            ((FutureMessage)future).Respond(new FutureError<T>(e, _pid, header));
        }
    }
}
