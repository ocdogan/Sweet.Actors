using System;
using System.Collections.Generic;
using System.Text;

namespace Sweet.Actors
{
    public interface IErrorHandler
    {
        void HandleError(IProcess process, IMessage msg, Exception error);
    }

    internal class DefaultErrorHandler : IErrorHandler
    {
        public static readonly IErrorHandler Instance = new DefaultErrorHandler();

        public void HandleError(IProcess process, IMessage msg, Exception error)
        {
        }
    }
}
