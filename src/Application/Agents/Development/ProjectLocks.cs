using System.Collections.Concurrent;
using DevAgents.Application.Common.Exceptions;

namespace DevAgents.Application.Agents.Development;

/// <summary>
/// Aynı projede aynı anda tek bir yazma işi (geliştirme ya da geri alma) çalışsın diye proje başına kilit.
/// İki geliştirici aynı projeye aynı anda kod yazdırırsa ikincisi beklemek yerine hemen bilgilendirilir.
/// </summary>
public sealed class ProjectLocks
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks =
        new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    /// <exception cref="ProjectBusyException">Projede başka bir iş sürüyor.</exception>
    public IDisposable Acquire(string projectRoot)
    {
        var key = Path.GetFullPath(projectRoot).TrimEnd(Path.DirectorySeparatorChar);
        var semaphore = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        if (!semaphore.Wait(0))
            throw new ProjectBusyException(projectRoot);
        return new Release(semaphore);
    }

    private sealed class Release(SemaphoreSlim semaphore) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                semaphore.Release();
        }
    }
}
