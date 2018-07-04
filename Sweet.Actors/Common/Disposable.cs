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
            if (Common.CompareAndSet(ref _disposed, false, true))
                OnDispose(false);
        }

		public bool Disposed => (_disposed != Constants.False);

        protected virtual void ThrowIfDisposed(string name = null)
        {
            if (Disposed)
            {
                name = name?.Trim();
                throw new ObjectDisposedException(String.IsNullOrEmpty(name) ? GetType().Name : name);
            }
        }

        protected void SuppressFinalizeOnDispose(bool value)
        {
            _doNotSuppressFinalizeOnDispose = !value;
        }

        public void Dispose()
        {
            if (Common.CompareAndSet(ref _disposed, false, true))
            {
                OnDispose(true);
                if (!_doNotSuppressFinalizeOnDispose)
                    GC.SuppressFinalize(this);
            }
        }

        protected virtual void OnDispose(bool disposing)
        { }
    }
}
