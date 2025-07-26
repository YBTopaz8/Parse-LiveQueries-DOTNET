using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
        }
        catch (Exception ex)
        {
            return Task.FromException(ex);
        }
        return Task.CompletedTask;
    }

    public Task EnqueueOnSuccess<TIn>(Task<TIn> task, Func<Task<TIn>, Task> onSuccess)
    {
        // In a synchronous test, we assume the input task is already completed.
        if (task.IsFaulted || task.IsCanceled)
        {
            return task;
        }
        return onSuccess(task);
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
