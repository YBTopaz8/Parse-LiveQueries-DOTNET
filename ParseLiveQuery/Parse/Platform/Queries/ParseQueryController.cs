using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Parse.Abstractions.Infrastructure.Data;
using Parse.Abstractions.Infrastructure.Execution;
using Parse.Abstractions.Platform.Objects;
using Parse.Abstractions.Platform.Queries;
using Parse.Infrastructure.Data;
using Parse.Infrastructure.Execution;
using Parse.Infrastructure.Utilities;

namespace Parse.Platform.Queries;

/// <summary>
/// A straightforward implementation of <see cref="IParseQueryController"/> that uses <see cref="ParseObject.Services"/> to decode raw server data when needed.
/// </summary>

internal class ParseQueryController : IParseQueryController
{
    private IParseCommandRunner CommandRunner { get; }
    private IParseDataDecoder Decoder { get; }

    public ParseQueryController(IParseCommandRunner commandRunner, IParseDataDecoder decoder)
    {
        CommandRunner = commandRunner;
        Decoder = decoder;
    }

    public async Task<IEnumerable<IObjectState>> FindAsync<T>(ParseQuery<T> query, ParseUser user, CancellationToken cancellationToken = default) where T : ParseObject
    {
        var result = await FindAsync(query.ClassName, query.BuildParameters(), user?.SessionToken, cancellationToken).ConfigureAwait(false);

        // Check if the result contains an error code
        if (result.TryGetValue("code", out object? codeValue) && codeValue is long errorCode)
        {
            if (errorCode == 102) // Specific handling for "Cannot query on ACL"
            {
                throw new InvalidOperationException("Cannot query on ACL. Ensure your query does not filter by ACL.");
            }

            // Handle other error codes here if needed
        }

        // Process raw results
        var rawResults = result.TryGetValue("results", out object? results) ? results as IList<object> : new List<object>();
        if (rawResults is null || rawResults.Count == 0)
        {
            return Enumerable.Empty<IObjectState>();
        }

        return rawResults
            .Select(item => ParseObjectCoder.Instance.Decode(item as IDictionary<string, object>, Decoder, user?.Services));
    }


    public async Task<int> CountAsync<T>(ParseQuery<T> query, ParseUser user, CancellationToken cancellationToken = default) where T : ParseObject
    {
        var parameters = query.BuildParameters();
        parameters["limit"] = 0;
        parameters["count"] = 1;

        var result = await FindAsync(query.ClassName, parameters, user?.SessionToken, cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(result["count"]);
    }

    public async Task<IObjectState> FirstAsync<T>(ParseQuery<T> query, ParseUser user, CancellationToken cancellationToken = default) where T : ParseObject
    {
        var parameters = query.BuildParameters();
        parameters["limit"] = 1;

        var result = await FindAsync(query.ClassName, parameters, user?.SessionToken, cancellationToken).ConfigureAwait(false);
        var rawResults = result["results"] as IList<object> ?? new List<object>();

        var firstItem = rawResults.FirstOrDefault() as IDictionary<string, object>;
        return firstItem != null ? ParseObjectCoder.Instance.Decode(firstItem, Decoder, user?.Services) : null;
    }

    private async Task<IDictionary<string, object>> FindAsync(string className, IDictionary<string, object> parameters, string sessionToken, CancellationToken cancellationToken = default)
    {
        var command = new ParseCommand(
            $"classes/{Uri.EscapeDataString(className)}?{ParseClient.BuildQueryString(parameters)}",
            method: "GET",
            sessionToken: sessionToken,
            data: null
        );

        var response = await CommandRunner.RunCommandAsync(command, null,null,cancellationToken).ConfigureAwait(false);
        return response.Item2;
    }

    public async Task<IEnumerable<IDictionary<string, object>>?> AggregateAsync<T>(ParseQuery<T> query, IList<object> pipeline, ParseUser user, CancellationToken cancellationToken = default) where T : ParseObject
    {
        var parameters = query.BuildParameters();
        // Remove standard find parameters that don't apply to pipelines
        parameters.Remove("limit");
        parameters.Remove("skip");
        parameters.Remove("order");
        parameters.Remove("keys");
        parameters.Remove("include");

        parameters["pipeline"] = pipeline;

        var command = new ParseCommand(
            $"aggregate/{Uri.EscapeDataString(query.ClassName)}?{ParseClient.BuildQueryString(parameters)}",
            method: "GET",
            sessionToken: user?.SessionToken,
            data: null
        );

        var response = await CommandRunner.RunCommandAsync(command, null, null, cancellationToken).ConfigureAwait(false);
        var rawResults = response.Item2.TryGetValue("results", out object? results) ? results as IList<object> : new List<object>();

        return rawResults?.Select(r => Conversion.As<IDictionary<string, object>>(r));
    }

    public async Task<IEnumerable<TResult>?> DistinctAsync<T, TResult>(ParseQuery<T> query, string key, ParseUser? user, CancellationToken cancellationToken = default) where T : ParseObject
    {
        var parameters = query.BuildParameters();
        parameters["distinct"] = key;

        var command = new ParseCommand(
            $"aggregate/{Uri.EscapeDataString(query.ClassName)}?{ParseClient.BuildQueryString(parameters)}",
            method: "GET",
            sessionToken: user?.SessionToken,
            data: null
        );

        var response = await CommandRunner.RunCommandAsync(command, null, null, cancellationToken).ConfigureAwait(false);
        var rawResults = response.Item2.TryGetValue("results", out object? results) ? results as IList<object> : new List<object>();

        return rawResults?.Select(r => Conversion.To<TResult>(r));
    }
}