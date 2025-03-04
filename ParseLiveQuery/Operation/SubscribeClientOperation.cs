using System.Collections.Generic;
using Parse.Abstractions.Internal;

//using Parse.Common.Internal;
//using Parse.Core.Internal;
using Parse.Infrastructure.Data;

namespace Parse.LiveQuery;
public class SubscribeClientOperation<T> : SessionClientOperation where T : ParseObject
{
    private static readonly Dictionary<string, object> EmptyJsonObject = new(0);

    private readonly int _requestId;
    private readonly ExtendedParseQuery<T> _query;

    internal SubscribeClientOperation(Subscription<T> subscription, string sessionToken) : base(sessionToken)
    {
        _requestId = subscription.RequestID;
        _query = new ExtendedParseQuery<T>((ParseQuery<T>)subscription.QueryObj);
    }

    protected override IDictionary<string, object> ToJsonObject()
    {
        var queryJson = new Dictionary<string, object>
        {
            ["className"] = _query.ToParseQuery().GetClassName(),
            ["where"] = PointerOrLocalIdEncoder.Instance.Encode(_query.BuildParameters().ContainsKey("where") ? _query.BuildParameters()["where"]: EmptyJsonObject,
                ParseClient.Instance.Services)
        };

        // Add `fields` if specified
        if (_query.Fields.Count > 0)
        {
            queryJson["fields"] = _query.Fields;
        }

        return new Dictionary<string, object>
        {
            ["op"] = "subscribe",
            ["requestId"] = _requestId,
            ["query"] = queryJson
        };
    }
}
public class ExtendedParseQuery<T> where T : ParseObject
{
    private readonly ParseQuery<T> _query;

    public List<string> Fields { get; } = new List<string>();

    public ExtendedParseQuery(ParseQuery<T> query)
    {
        _query = query;
    }

    public ExtendedParseQuery<T> WhereEqualTo(string key, object value)
    {
        _query.WhereEqualTo(key, value);
        return this;
    }

    public ExtendedParseQuery<T> SelectKeys(IEnumerable<string> keys)
    {
        Fields.AddRange(keys);
        return this;
    }

    public ParseQuery<T> ToParseQuery() => _query;

    public IDictionary<string, object> BuildParameters() => _query.BuildParameters();
}
