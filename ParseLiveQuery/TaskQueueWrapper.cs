using Parse.Infrastructure.Utilities;
using Parse.LiveQuery;
using System;
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

    public Task EnqueueOnSuccess<TIn>(Task<TIn> task, Func<Task<TIn>, Task> onSuccess)
    {
        return _underlying.Enqueue(async cancellationToken => 
        {
            try
            {
                await task.ConfigureAwait(false); 
                await onSuccess(task).ConfigureAwait(false); 
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Error during EnqueueOnSuccess execution", ex);
            }
        }, CancellationToken.None); 
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