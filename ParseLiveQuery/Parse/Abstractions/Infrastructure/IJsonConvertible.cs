namespace Parse.Abstractions.Infrastructure;

/// <summary>
/// Represents an object that can be converted into JSON.
/// </summary>
public interface IJsonConvertible
{
    /// <summary>
    /// Converts the object to a data structure that can be converted to JSON.
    /// </summary>
    /// <returns>An object to be JSONified.</returns>
    object ConvertToJSON(IServiceHub serviceHub = default);
}
