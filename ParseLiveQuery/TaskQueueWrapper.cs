using Parse.Infrastructure.Utilities;
using Parse.LiveQuery;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace YB.Parse.LiveQuery;
internal class TaskQueueWrapper : ITaskQueue
{
    private readonly TaskQueue _underlying = new();

    public async Task Enqueue(Action taskStart)
    {
        await _underlying.Enqueue(async _ =>
        {
            taskStart();
            await Task.CompletedTask;
        }, CancellationToken.None);
    }

    public async Task EnqueueOnSuccess<TIn>(Task<TIn> task, Func<Task<TIn>, Task> onSuccess)
    {
        try
        {
            await task.ConfigureAwait(false);
            await onSuccess(task).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Error in EnqueueOnSuccess", ex);
        }
    }

    public async Task EnqueueOnError(Task task, Action<Exception> onError)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            onError(ex);
        }
    }
}