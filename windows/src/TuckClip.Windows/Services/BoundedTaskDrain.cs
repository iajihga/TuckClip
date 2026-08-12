using System.Diagnostics;

namespace TuckClip.Windows.Services;

internal static class BoundedTaskDrain
{
    public static async Task<bool> WaitUntilIdleAsync(
        Func<IReadOnlyList<Task>> getPendingTasks,
        TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(getPendingTasks);
        ArgumentOutOfRangeException.ThrowIfLessThan(timeout, TimeSpan.Zero);

        var startedAt = Stopwatch.GetTimestamp();
        while (true)
        {
            var pending = getPendingTasks()
                .Where(static task => !task.IsCompleted)
                .ToArray();
            if (pending.Length == 0)
            {
                return true;
            }

            var remaining = timeout - Stopwatch.GetElapsedTime(startedAt);
            if (remaining <= TimeSpan.Zero)
            {
                return false;
            }

            try
            {
                await Task.WhenAll(pending).WaitAsync(remaining).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                return false;
            }
            catch (Exception)
            {
                // Faulted and cancelled tasks are terminal. The caller tracks
                // their errors separately; continue in case new work appeared
                // while this snapshot was draining.
            }
        }
    }
}
