using System;
using System.Threading;

namespace Sweet.Actors
{
    public abstract class Disposable : IDisposable
    {
        private int _disposed;
        private bool _doNotSuppressFinalizeOnDispose;

        ~Disposable()
        {
            Dispose(false);
        }

		public bool Disposed => (_disposed != Constants.False);

        protected virtual void ThrowIfDisposed(string name = null)
        {
            if (Disposed)
                 throw new ObjectDisposedException(String.IsNullOrEmpty(name) ? GetType().Name : name);
        }

        protected void SuppressFinalizeOnDispose(bool value)
        {
            _doNotSuppressFinalizeOnDispose = !value;
        }

        public void Dispose()
        {
            Dispose(true);
            if (!_doNotSuppressFinalizeOnDispose)
                GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (Common.CompareAndSet(ref _disposed, false, true))
                OnDispose(disposing);
        }

        protected virtual void OnDispose(bool disposing)
        { }
    }
}
