using System;
using System.Collections.Generic;
using Parse.Abstractions.Infrastructure;
using Parse.Abstractions.Infrastructure.Control;
using Parse.Infrastructure.Data;

namespace Parse.Infrastructure.Control;

public class ParseSetOperation : IParseFieldOperation
{
    public ParseSetOperation(object value)
    {
        Value = value;
    }

    // Replace Encode with ConvertToJSON
    public object ConvertToJSON(IServiceHub serviceHub = default)
    {
        if (serviceHub == null)
        {
            throw new InvalidOperationException("ServiceHub is to encode the value.");
        }

        var encodedValue = PointerOrLocalIdEncoder.Instance.Encode(Value, serviceHub);

        // For simple values, return them directly (avoid unnecessary __op)
        if (Value != null && (Value.GetType().IsPrimitive || Value is string))
        {
            return Value;
        }

        // If the encoded value is a dictionary, return it directly
        if (encodedValue is IDictionary<string, object> dictionary)
        {
            return dictionary;
        }

        if (Value == null)
        {
            return null;
        }
        // Default behavior for unsupported types
        throw new ArgumentException($"Unsupported type for encoding: {Value?.GetType()?.FullName}");
    }



    public IParseFieldOperation MergeWithPrevious(IParseFieldOperation previous)
    {
        // Set operation always overrides previous operations
        return this;
    }

    public object Apply(object oldValue, string key)
    {
        // Set operation always sets the field to the specified value
        return Value;
    }

    public object Value { get; private set; }
}
