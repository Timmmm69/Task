using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Task.Desktop.Personal;

internal sealed class PersonalStoreBusyException : IOException
{
    public PersonalStoreBusyException() : base("Personal открыт в другом окне Task. Доступны Corporate и выход.") { }
}

/// <summary>A dedicated thread owns the Windows mutex, including across async continuations.</summary>
internal sealed class PersonalStoreOwnership : IDisposable
{
    private readonly ManualResetEventSlim _release = new();
    private readonly Thread _thread;
    private int _disposed;
    internal string DatabasePath { get; }
    private PersonalStoreOwnership(string path)
    {
        DatabasePath = Path.GetFullPath(path);
        var name = @"Global\Task.Personal." + Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(DatabasePath.ToUpperInvariant())));
        using var ready = new ManualResetEventSlim();
        Exception? failure = null;
        _thread = new Thread(() =>
        {
            var acquired = false;
            try
            {
                using var mutex = new Mutex(false, name);
                try { acquired = mutex.WaitOne(0); }
                catch (AbandonedMutexException) { acquired = true; }
                if (!acquired) throw new PersonalStoreBusyException();
                ready.Set();
                _release.Wait();
                mutex.ReleaseMutex();
            }
            catch (Exception error) { failure = error; ready.Set(); }
        })
        { IsBackground = true, Name = "Personal store ownership" };
        _thread.Start();
        ready.Wait();
        if (failure is not null) { _thread.Join(); _release.Dispose(); throw failure; }
    }
    internal static PersonalStoreOwnership Acquire(PersonalDataPaths paths) => new(paths.DatabasePath);
    internal void Verify(PersonalDataPaths paths)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (!string.Equals(DatabasePath, paths.DatabasePath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Personal ownership path mismatch.");
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _release.Set(); _thread.Join(); _release.Dispose();
    }
}
