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
    public interface IErrorHandler
    {
        void HandleError(ActorSystem actorSystem, Exception error);
        void HandleProcessError(ActorSystem actorSystem, Pid pid, IMessage message, Exception error);
    }

    internal class DefaultErrorHandler : IErrorHandler
    {
        public static readonly IErrorHandler Instance = new DefaultErrorHandler();

        public void HandleError(ActorSystem actorSystem, Exception error)
        { }

        public void HandleProcessError(ActorSystem actorSystem, Pid pid, IMessage message, Exception error)
        { }
    }

    internal class ErrorHandlerAction : IErrorHandler
    {
        private Action<ActorSystem, Exception> _generalErrorHandler;
        private Action<ActorSystem, Pid, IMessage, Exception> _processErrorHandler;

        internal ErrorHandlerAction(Action<ActorSystem, Exception> generalErrorHandler,
            Action<ActorSystem, Pid, IMessage, Exception> processErrorHandler)
        {
            _generalErrorHandler = _generalErrorHandler ?? DefaultErrorHandler.Instance.HandleError;
            _processErrorHandler = processErrorHandler ?? DefaultErrorHandler.Instance.HandleProcessError;
        }

        public void HandleError(ActorSystem actorSystem, Exception error)
        {
            _generalErrorHandler(actorSystem, error);
        }

        public void HandleProcessError(ActorSystem actorSystem, Pid pid, IMessage message, Exception error)
        {
            _processErrorHandler(actorSystem, pid, message, error);
        }
    }
}
