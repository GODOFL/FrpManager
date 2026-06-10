using System.Threading;

namespace FrpManager.Helpers
{
    /// <summary>
    /// Keeps FrpManager single-instance within the current Windows logon session.
    /// The returned guard must stay alive until application shutdown; disposing it
    /// releases the named mutex for the next launch.
    /// </summary>
    public sealed class SingleInstanceGuard : IDisposable
    {
        private const string MutexName = @"Local\GODOFL.FrpManager.SingleInstance";
        private readonly Mutex _mutex;
        private bool _disposed;

        private SingleInstanceGuard(Mutex mutex)
        {
            _mutex = mutex;
        }

        /// <summary>
        /// Acquires the single-instance mutex, or returns null when another
        /// FrpManager process is already running.
        /// </summary>
        public static SingleInstanceGuard? TryAcquire()
        {
            var mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
            if (createdNew)
                return new SingleInstanceGuard(mutex);

            mutex.Dispose();
            return null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try { _mutex.ReleaseMutex(); }
            catch (ApplicationException) { /* Mutex ownership was already released. */ }
            _mutex.Dispose();
        }
    }
}
