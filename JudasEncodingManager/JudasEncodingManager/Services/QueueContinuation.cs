using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace JudasEncodingManager.Services
{
    /// <summary>
    /// Runs synchronous queue cleanup without making the caller wait on filesystem work.
    /// </summary>
    public static class QueueCleanupScheduler
    {
        public static Task RunAsync(Action cleanup)
        {
            ArgumentNullException.ThrowIfNull(cleanup);
            return Task.Run(cleanup);
        }
    }

    /// <summary>
    /// Runs pending work one item at a time. An item failure is reported to the
    /// caller and does not prevent later pending items from running.
    /// </summary>
    public static class QueueContinuation
    {
        public static async Task ProcessPendingAsync<T>(
            IEnumerable<T> items,
            Func<T, bool> isPending,
            Func<T, CancellationToken, Task> process,
            Action<T, Exception> onFailure,
            CancellationToken cancellationToken = default)
            where T : class
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var nextItem = items.FirstOrDefault(isPending);
                if (nextItem is null)
                    return;

                try
                {
                    await process(nextItem, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    onFailure(nextItem, ex);
                }
            }
        }
    }
}