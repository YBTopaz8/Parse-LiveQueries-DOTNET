using System;
using System.Threading.Tasks;

namespace Parse.LiveQuery.Tests.ParseLiveQueries.Tests;

/// <summary>
/// An implementation of ITaskQueue that executes tasks immediately and synchronously.
/// This is essential for making unit tests deterministic and predictable, removing
/// race conditions caused by real async behavior.
/// </summary>
internal class SynchronousTaskQueue : ITaskQueue
{
    public Task Enqueue(Action taskStart)
    {
        try
        {
            taskStart();
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            return Task.FromException(ex);
        }
    }

    public async Task EnqueueOnSuccess<TIn>(Func<Task<TIn>> taskFactory, Func<Task<TIn>, Task> onSuccess)
    {
        Task<TIn> task;

        try
        {
            // 1. Execute the factory to start the task
            task = taskFactory();

            // 2. Await the task synchronously in the queue
            await task.ConfigureAwait(false);
        }
        catch
        {
            // If the factory throws, or the task faults/cancels, we skip the onSuccess callback.
            // We rethrow so the unit test catches the failure and fails appropriately.
            throw;
        }

        // 3. If we reach here, the task succeeded. Execute the success callback!
        await onSuccess(task).ConfigureAwait(false);
    }

    public Task EnqueueOnError(Task task, Action<Exception> onError)
    {
        if (task.IsFaulted)
        {
            // Unwrap the AggregateException if it exists.
            var exceptionToReport = task.Exception?.InnerException ?? task.Exception;
            if (exceptionToReport != null)
            {
                onError(exceptionToReport);
            }
        }

        return Task.CompletedTask;
    }
}