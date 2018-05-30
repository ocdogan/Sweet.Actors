using System;
using System.Threading;

namespace Sweet.Actors
{
    public abstract class Disposable : IDisposable
    {
        private int _disposed;

        ~Disposable()
        {
            Dispose(false);
        }

        public bool Disposed => _disposed != Common.False;

        protected virtual void ThrowIfDisposed(string name = null)
        {
            if (Disposed)
                 throw new ObjectDisposedException(String.IsNullOrEmpty(name) ? GetType().Name : name);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (Common.CompareAndSet(ref _disposed, false, true))
            {
                OnDispose(disposing);
            }
        }

        protected virtual void OnDispose(bool disposing)
        { }
    }
}
