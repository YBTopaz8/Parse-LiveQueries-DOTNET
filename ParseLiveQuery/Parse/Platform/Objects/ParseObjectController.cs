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

    public async Task<IObjectState?> FetchAsync(IObjectState state, string sessionToken, IServiceHub serviceHub, CancellationToken cancellationToken = default)
    {
        var command = new ParseCommand($"classes/{Uri.EscapeDataString(state.ClassName)}/{Uri.EscapeDataString(state.ObjectId)}", method: "GET", sessionToken: sessionToken, data: null);

        var result = await CommandRunner.RunCommandAsync(command, cancellationToken: cancellationToken).ConfigureAwait(false);
        return ParseObjectCoder.Instance.Decode(result.Item2, Decoder, serviceHub);
    }


    public async Task<IObjectState?> SaveAsync(IObjectState state, IDictionary<string, IParseFieldOperation>? operations, string sessionToken, IServiceHub serviceHub, CancellationToken cancellationToken = default)
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
        if (result?.Item1 == System.Net.HttpStatusCode.Gone)
        {
            throw new HttpRequestException("Page does not exist");
        }


        var decodedState = ParseObjectCoder.Instance.Decode(result.Item2, Decoder, serviceHub);

        decodedState = decodedState.MutatedClone(mutableClone =>
            mutableClone.IsNew = result.Item1 == System.Net.HttpStatusCode.Created);

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

        // 2. Execute via the clean async batch executor
        var batchResults = await ExecuteBatchRequestsAsync(requests, sessionToken, cancellationToken).ConfigureAwait(false);

        // 3. Decode the raw response dictionaries back into IObjectStates
        var decodedStates = new List<IObjectState>(statesList.Count);
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

    public async Task<bool> DeleteAllAsync(
    IEnumerable<IObjectState> states,
    string sessionToken,
    CancellationToken cancellationToken = default)
    {
        var statesList = states.Where(item => item.ObjectId is not null).ToList();
        if (statesList.Count == 0)
            return true;

        // 1. Build the DELETE commands
        var requests = statesList.Select(item => new ParseCommand(
            $"classes/{Uri.EscapeDataString(item.ClassName)}/{Uri.EscapeDataString(item.ObjectId)}",
            method: "DELETE",
            data: null
        )).ToList();

        // 2. Execute batches cleanly with async/await
        var batchResults = await ExecuteBatchRequestsAsync(requests, sessionToken, cancellationToken).ConfigureAwait(false);

        // Returns true if all items in the batch were processed successfully
        return batchResults.Count == statesList.Count;
    }

    int MaximumBatchSize { get; } = 50;

    internal async Task<IList<IDictionary<string, object>>> ExecuteBatchRequestsAsync(
        IList<ParseCommand> requests,
        string sessionToken,
        CancellationToken cancellationToken = default)
    {
        var allResults = new List<IDictionary<string, object>>();

        // Modern .NET 6+ Chunking
        foreach (var batch in requests.Chunk(MaximumBatchSize))
        {
            var batchResults = await ExecuteSingleBatchAsync(batch.ToList(), sessionToken, cancellationToken).ConfigureAwait(false);
            allResults.AddRange(batchResults);
        }

        return allResults;
    }

    private async Task<IList<IDictionary<string, object>>> ExecuteSingleBatchAsync(
        IList<ParseCommand> requests,
        string sessionToken,
        CancellationToken cancellationToken)
    {
        int batchSize = requests.Count;
        if (batchSize == 0)
            return Array.Empty<IDictionary<string, object>>();

        var encodedRequests = requests.Select(request =>
        {
            var resultDict = new Dictionary<string, object>
            {
                ["method"] = request.Method,
                ["path"] = request is { Path: { }, Resource: { } }
                    ? request.Target.AbsolutePath
                    : new Uri(new Uri(ServerConnectionData.ServerURI), request.Path).AbsolutePath,
            };

            if (request.DataObject != null)
                resultDict["body"] = request.DataObject;

            return (object)resultDict;
        }).ToList();

        var batchCommand = new ParseCommand("batch", method: "POST", sessionToken: sessionToken, data: new Dictionary<string, object>
        {
            ["requests"] = encodedRequests
        });

        var response = await CommandRunner.RunCommandAsync(batchCommand, cancellationToken: cancellationToken).ConfigureAwait(false);

        if (response.Item2 == null || !response.Item2.TryGetValue("results", out var rawResults) || rawResults is not IList<object> resultsArray)
        {
            throw new ParseFailureException(ParseFailureException.ErrorCode.OtherCause, "Invalid batch response received from Parse Server.");
        }

        if (resultsArray.Count != batchSize)
        {
            throw new InvalidOperationException($"Batch command expected {batchSize} results, but received {resultsArray.Count}.");
        }

        var output = new List<IDictionary<string, object>>(batchSize);

        for (int i = 0; i < batchSize; i++)
        {
            if (resultsArray[i] is not IDictionary<string, object> resultDict)
            {
                throw new InvalidOperationException("Invalid item format in batch response.");
            }

            if (resultDict.TryGetValue("success", out var successObj) && successObj is IDictionary<string, object> successDict)
            {
                output.Add(successDict);
            }
            else if (resultDict.TryGetValue("error", out var errorObj) && errorObj is IDictionary<string, object> errorDict)
            {
                var code = errorDict.TryGetValue("code", out var c) ? Convert.ToInt64(c) : (long)ParseFailureException.ErrorCode.OtherCause;
                var message = errorDict.TryGetValue("error", out var msg) ? msg?.ToString() : "Unknown batch error occurred.";
                throw new ParseFailureException((ParseFailureException.ErrorCode)code, message);
            }
            else
            {
                // Handles simple cases like { "success": true }
                output.Add(resultDict);
            }
        }

        return output;
    }
}
