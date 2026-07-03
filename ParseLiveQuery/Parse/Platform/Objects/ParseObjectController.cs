using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Parse.Abstractions.Infrastructure.Control;
using Parse.Abstractions.Infrastructure.Data;
using Parse.Abstractions.Infrastructure.Execution;
using Parse.Abstractions.Infrastructure;
using Parse.Abstractions.Platform.Objects;
using Parse.Infrastructure.Utilities;
using Parse.Infrastructure;
using Parse.Abstractions.Internal;
using Parse.Infrastructure.Execution;
using Parse.Infrastructure.Data;
using System.Net.Http;

namespace Parse.Platform.Objects;

public class ParseObjectController : IParseObjectController
{
    IParseCommandRunner CommandRunner { get; }

    IParseDataDecoder Decoder { get; }

    IServerConnectionData ServerConnectionData { get; }

    public ParseObjectController(IParseCommandRunner commandRunner, IParseDataDecoder decoder, IServerConnectionData serverConnectionData) => (CommandRunner, Decoder, ServerConnectionData) = (commandRunner, decoder, serverConnectionData);

    public async Task<IObjectState> FetchAsync(IObjectState state, string sessionToken, IServiceHub serviceHub, CancellationToken cancellationToken = default)
    {
        var command = new ParseCommand($"classes/{Uri.EscapeDataString(state.ClassName)}/{Uri.EscapeDataString(state.ObjectId)}", method: "GET", sessionToken: sessionToken, data: null);

        var result = await CommandRunner.RunCommandAsync(command, cancellationToken: cancellationToken).ConfigureAwait(false);
        return ParseObjectCoder.Instance.Decode(result.Item2, Decoder, serviceHub);
    }


    public async Task<IObjectState> SaveAsync(IObjectState state, IDictionary<string, IParseFieldOperation> operations, string sessionToken, IServiceHub serviceHub, CancellationToken cancellationToken = default)
    {
        ParseCommand command;
        if (state.ObjectId == null)
        {
            var method = "POST";
            var relURI = $"classes/{Uri.EscapeDataString(state.ClassName)}";
            var dataa = serviceHub.GenerateJSONObjectForSaving(operations);
            command = new ParseCommand(relURI, method, sessionToken: sessionToken, data: dataa);
        }
        else
        {
            var method = "PUT";
            var relURI = $"classes/{Uri.EscapeDataString(state.ClassName)}/{state.ObjectId}";
            var dataa = serviceHub.GenerateJSONObjectForSaving(operations);
            command = new ParseCommand(relURI, method, sessionToken: sessionToken, data: dataa);
        }
        var result = await CommandRunner.RunCommandAsync(command, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (result.Item1 == System.Net.HttpStatusCode.Gone)
        {
            throw new HttpRequestException("Page does not exist");
        }
        var decodedState = ParseObjectCoder.Instance.Decode(result.Item2, Decoder, serviceHub);
        
        // Mutating the state and marking it as new if the status code is Created
        decodedState.MutatedClone(mutableClone => mutableClone.IsNew = result.Item1 == System.Net.HttpStatusCode.Created);

        return decodedState;
    }


    public async Task<IEnumerable<IObjectState>> SaveAllAsync(
      IEnumerable<IObjectState> states,
      IEnumerable<IDictionary<string, IParseFieldOperation>> operationsList,
      string sessionToken,
      IServiceHub serviceHub,
      CancellationToken cancellationToken = default)
    {
        var statesList = states.ToList();
        var opsList = operationsList.ToList();

        if (statesList.Count == 0)
        {
            return [];
        }

        // 1. Build the individual batch command payloads (POST for new, PUT for updates)
        var requests = statesList.Zip(opsList, (state, operations) =>
        {
            var isNew = state.ObjectId == null;
            var path = isNew
                ? $"classes/{Uri.EscapeDataString(state.ClassName)}"
                : $"classes/{Uri.EscapeDataString(state.ClassName)}/{Uri.EscapeDataString(state.ObjectId)}";

            return new ParseCommand(
                path,
                method: isNew ? "POST" : "PUT",
                sessionToken: sessionToken,
                data: serviceHub.GenerateJSONObjectForSaving(operations)
            );
        }).ToList();

        // 2. Execute via the batch endpoint (/batch) to save network overhead
        var batchTasks = ExecuteBatchRequests(requests, sessionToken, cancellationToken);
        var batchResults = await Task.WhenAll(batchTasks).ConfigureAwait(false);

        // 3. Decode the raw response dictionaries back into IObjectStates
        var decodedStates = new List<IObjectState>();
        for (int i = 0; i < statesList.Count; i++)
        {
            var resultDict = batchResults[i];
            var decoded = ParseObjectCoder.Instance.Decode(resultDict, Decoder, serviceHub);

            // Ensure the decoded state retains the class name and ID if the batch response omitted them
            if (decoded.ClassName == null)
            {
                decoded = decoded.MutatedClone(mutable =>
                {
                    mutable.ClassName = statesList[i].ClassName;
                    if (statesList[i].ObjectId != null)
                    {
                        mutable.ObjectId = statesList[i].ObjectId;
                    }
                });
            }
            decodedStates.Add(decoded);
        }

        return decodedStates;
    }


    public async Task<bool> DeleteAsync(IObjectState state, string sessionToken, CancellationToken cancellationToken = default)
    {
        if (state.ObjectId == null || state.ClassName is null)
        {
            return false;
        }

        try
        {
            var command = new ParseCommand(
                $"classes/{Uri.EscapeDataString(state.ClassName)}/{Uri.EscapeDataString(state.ObjectId)}",
                method: "DELETE",
                sessionToken: sessionToken,
                data: null
            );

            await CommandRunner.RunCommandAsync(command, cancellationToken: cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (ParseFailureException ex) when (ex.Code == ParseFailureException.ErrorCode.ObjectNotFound)
        {
            // Object didn't exist on the server anyway; return false to indicate no deletion occurred
            return false;
        }
    }
   

    public IEnumerable<Task> DeleteAllAsync(IEnumerable<IObjectState> states, string sessionToken, CancellationToken cancellationToken = default)
    {
        return ExecuteBatchRequests(states.Where(item => item.ObjectId is { }).Select(item => new ParseCommand($"classes/{Uri.EscapeDataString(item.ClassName)}/{Uri.EscapeDataString(item.ObjectId)}", method: "DELETE", data: default)).ToList(), sessionToken, cancellationToken).Cast<Task>().ToList();
    }

    int MaximumBatchSize { get; } = 50;

    // TODO (hallucinogen): move this out to a class to be used by Analytics

    internal IList<Task<IDictionary<string, object>>> ExecuteBatchRequests(IList<ParseCommand> requests, string sessionToken, CancellationToken cancellationToken = default)
    {
        List<Task<IDictionary<string, object>>> tasks = new List<Task<IDictionary<string, object>>>();
        int batchSize = requests.Count;

        IEnumerable<ParseCommand> remaining = requests;

        while (batchSize > MaximumBatchSize)
        {
            List<ParseCommand> process = remaining.Take(MaximumBatchSize).ToList();

            remaining = remaining.Skip(MaximumBatchSize);
            tasks.AddRange(ExecuteBatchRequest(process, sessionToken, cancellationToken));
            batchSize = remaining.Count();
        }

        tasks.AddRange(ExecuteBatchRequest(remaining.ToList(), sessionToken, cancellationToken));
        return tasks;
    }

    IList<Task<IDictionary<string, object>>> ExecuteBatchRequest(IList<ParseCommand> requests, string sessionToken, CancellationToken cancellationToken = default)
    {
        int batchSize = requests.Count;

        List<Task<IDictionary<string, object>>> tasks = new List<Task<IDictionary<string, object>>> { };
        List<TaskCompletionSource<IDictionary<string, object>>> completionSources = new List<TaskCompletionSource<IDictionary<string, object>>> { };

        for (int i = 0; i < batchSize; ++i)
        {
            TaskCompletionSource<IDictionary<string, object>> tcs = new TaskCompletionSource<IDictionary<string, object>>();

            completionSources.Add(tcs);
            tasks.Add(tcs.Task);
        }

        List<object> encodedRequests = requests.Select(request =>
        {
            Dictionary<string, object> results = new Dictionary<string, object>
            {
                ["method"] = request.Method,
                ["path"] = request is { Path: { }, Resource: { } } ? request.Target.AbsolutePath : new Uri(new Uri(ServerConnectionData.ServerURI), request.Path).AbsolutePath,
            };

            if (request.DataObject != null)
                results["body"] = request.DataObject;

            return results;
        }).Cast<object>().ToList();

        ParseCommand command = new ParseCommand("batch", method: "POST", sessionToken: sessionToken, data: new Dictionary<string, object> { [nameof(requests)] = encodedRequests });

        CommandRunner.RunCommandAsync(command, cancellationToken: cancellationToken).ContinueWith(task =>
        {
            if (task.IsFaulted || task.IsCanceled)
            {
                foreach (TaskCompletionSource<IDictionary<string, object>> tcs in completionSources)
                    if (task.IsFaulted)
                        tcs.TrySetException(task.Exception);
                    else if (task.IsCanceled)
                        tcs.TrySetCanceled();

                return;
            }

            IList<object>? resultsArray = Conversion.As<IList<object>>(task.Result?.Item2["results"]);
            int? resultLength = resultsArray?.Count;

            if (resultLength != batchSize)
            {
                foreach (TaskCompletionSource<IDictionary<string, object>> completionSource in completionSources)
                    completionSource.TrySetException(new InvalidOperationException($"Batch command result count expected: {batchSize} but was: {resultLength}."));

                return;
            }

            for (int i = 0; i < batchSize; ++i)
            {
                Dictionary<string, object>? result = resultsArray[i] as Dictionary<string, object>;
                TaskCompletionSource<IDictionary<string, object>> target = completionSources[i];

                if (result.ContainsKey("success"))
                    target.TrySetResult(result["success"] as IDictionary<string, object>);
                else if (result.ContainsKey("error"))
                {
                    IDictionary<string, object> error = result["error"] as IDictionary<string, object>;
                    target.TrySetException(new ParseFailureException((ParseFailureException.ErrorCode) (long) error["code"], error[nameof(error)] as string));
                }
                else
                    target.TrySetException(new InvalidOperationException("Invalid batch command response."));
            }
        });

        return tasks;
    }
}
