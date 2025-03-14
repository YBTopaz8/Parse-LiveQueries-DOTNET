using System;
using System.Collections.Generic;
using System.Diagnostics;
using Parse.Abstractions.Infrastructure;
using Parse.Abstractions.Infrastructure.Data;
using Parse.Infrastructure.Data;
using Parse.Infrastructure.Utilities;

namespace Parse.Platform.Configuration;

/// <summary>
/// The ParseConfig is a representation of the remote configuration object,
/// that enables you to add things like feature gating, a/b testing or simple "Message of the day".
/// </summary>
public class ParseConfiguration : IJsonConvertible
{
    IDictionary<string, object> Properties { get; } = new Dictionary<string, object> { };

    IServiceHub Services { get; }

    internal ParseConfiguration(IServiceHub serviceHub) => Services = serviceHub;

    ParseConfiguration(IDictionary<string, object> properties, IServiceHub serviceHub) : this(serviceHub) => Properties = properties;

    internal static ParseConfiguration Create(IDictionary<string, object> configurationData, IParseDataDecoder decoder, IServiceHub serviceHub)
    {
        return new ParseConfiguration(decoder.Decode(configurationData["params"], serviceHub) as IDictionary<string, object>, serviceHub);
    }

    /// <summary>
    /// Gets a value for the key of a particular type.
    /// </summary>
    /// <typeparam name="T">The type to convert the value to. Supported types are
    /// ParseObject and its descendents, Parse types such as ParseRelation and ParseGeopoint,
    /// primitive types,IList&lt;T&gt;, IDictionary&lt;string, T&gt; and strings.</typeparam>
    /// <param name="key">The key of the element to get.</param>
    /// <exception cref="KeyNotFoundException">The property is retrieved
    /// and <paramref name="key"/> is not found.</exception>
    /// <exception cref="System.FormatException">The property under this <paramref name="key"/>
    /// key was found, but of a different type.</exception>
    public T Get<T>(string key)
    {
        try
        {
            // Check if the key exists in the Properties dictionary
            if (!Properties.ContainsKey(key))
            {
                throw new KeyNotFoundException($"The key '{key}' was not found in the configuration.");
            }

            // Try to convert the value to the desired type
            return Conversion.To<T>(Properties[key]);
        }
        catch (KeyNotFoundException)
        {
            // Handle case where the key is not found in the dictionary
            throw;
        }
        catch (Exception ex)
        {
            // Handle any other exception, such as a FormatException when conversion fails
            throw new FormatException($"Error converting value for key '{key}' to type '{typeof(T)}'.", ex);
        }
    }

    /// <summary>
    /// Populates result with the value for the key, if possible.
    /// </summary>
    /// <typeparam name="T">The desired type for the value.</typeparam>
    /// <param name="key">The key to retrieve a value for.</param>
    /// <param name="result">The value for the given key, converted to the
    /// requested type, or null if unsuccessful.</param>
    /// <returns>true if the lookup and conversion succeeded, otherwise false.</returns>
    public bool TryGetValue<T>(string key, out T result)
    {
        result = default;

        try
        {
            // Check if the key exists in the Properties dictionary
            if (Properties.ContainsKey(key))
            {
                // Attempt to convert the value to the requested type
                result = Conversion.To<T>(Properties[key]);
                return true;
            }

            // If the key does not exist, return false
            return false;
        }
        catch (Exception ex)
        {
            // Log the exception if needed or just return false
            Debug.WriteLine($"Error converting value for key '{key}': {ex.Message}");
            return false;
        }
    }


    /// <summary>
    /// Gets a value on the config.
    /// </summary>
    /// <param name="key">The key for the parameter.</param>
    /// <exception cref="KeyNotFoundException">The property is
    /// retrieved and <paramref name="key"/> is not found.</exception>
    /// <returns>The value for the key.</returns>
    virtual public object this[string key] => Properties[key];

    public object ConvertToJSON(IServiceHub serviceHub = default)
    {
        return new Dictionary<string, object>
        {
            ["params"] = NoObjectsEncoder.Instance.Encode(Properties, Services)
        };
    }
}
