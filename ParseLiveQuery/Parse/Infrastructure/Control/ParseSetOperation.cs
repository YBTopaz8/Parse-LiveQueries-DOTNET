using System;
using System.Collections.Generic;
using Parse.Abstractions.Infrastructure;
using Parse.Abstractions.Infrastructure.Control;
using Parse.Infrastructure.Data;

namespace Parse.Infrastructure.Control;

public class ParseSetOperation : IParseFieldOperation
{
    public object Value { get; private set; }

    public ParseSetOperation(object value)
    {
        Value = value;
    }

    public IDictionary<string, object> ConvertToJSON(IServiceHub serviceHub = default)
    {
        if (serviceHub == null)
        {
            throw new InvalidOperationException("ServiceHub is required to encode the value.");
        }

        // Just let the encoder do its job
        var encodedValue = PointerOrLocalIdEncoder.Instance.Encode(Value, serviceHub);

        if (encodedValue is IDictionary<string, object> dictionary)
        {
            return dictionary;
        }

        if (Value is null)
        {
            throw new ArgumentNullException(nameof(Value));
        }

        // Fallback for primitive types/strings to avoid nesting
        return new Dictionary<string, object> { ["value"] = Value };
    }

    public IParseFieldOperation MergeWithPrevious(IParseFieldOperation previous)
    {
        return this; // Set always overrides
    }

    public object Apply(object oldValue, string key)
    {
        return Value;
    }

    public object ConvertValueToJSON(IServiceHub serviceHub = null)
    {
        if (serviceHub == null)
        {
            throw new InvalidOperationException("ServiceHub is required.");
        }

        // Standard Parse behavior: return the direct encoded value
        return PointerOrLocalIdEncoder.Instance.Encode(Value, serviceHub);
    }
}