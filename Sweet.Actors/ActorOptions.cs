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

namespace Sweet.Actors
{
    public sealed class ActorOptions
    {
        public static readonly ActorOptions Default = new ActorOptions(null);

        private string _name;
        private int _sequentialInvokeLimit = -1;
        private IDictionary<string, object> _initialContextData;

        private ActorOptions(string name)
        {
            _name = name?.Trim();
            if (String.IsNullOrEmpty(_name))
                _name = Constants.DefaultActorSystemName;
        }

        public static ActorOptions UsingName(string name)
        {
            return new ActorOptions(name);
        }

        public ActorOptions UsingSequentialInvokeLimit(int sequentialInvokeLimit)
        {
            _sequentialInvokeLimit = Common.ValidateSequentialInvokeLimit(sequentialInvokeLimit);
            return this;
        }

        public ActorOptions UsingInitialContextData(IDictionary<string, object> initialContextData)
        {
            _initialContextData = initialContextData;
            return this;
        }

        public string Name => _name;

        public int SequentialInvokeLimit => _sequentialInvokeLimit;

        public IDictionary<string, object> InitialContextData => _initialContextData;
    }
}
