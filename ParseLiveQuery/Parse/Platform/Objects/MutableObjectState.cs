using Parse.Abstractions.Infrastructure;
using Parse.Abstractions.Infrastructure.Control;
using Parse.Abstractions.Platform.Objects;
using Parse.Infrastructure;
using Parse.Infrastructure.Control;
using Parse.Infrastructure.Data;

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;

namespace Parse.Platform.Objects;

public class MutableObjectState : IObjectState
{
    public bool IsNew { get; set; }
    public string? ClassName { get; set; }
    public string? ObjectId { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? CreatedAt { get; set; }
    public string? SessionToken { get; set; } // Added

    public IDictionary<string, object?> ServerData { get; set; } = new Dictionary<string, object?>();
    public object? this[string key]
    {
        get => ServerData.ContainsKey(key) ? ServerData[key] : null;
        set
        {
            if (!Equals(ServerData[key], value))
            {
                ServerData[key] = value;
                OnPropertyChanged(key); // Raise PropertyChanged for the updated key
            }
        }
    }

    public event PropertyChangedEventHandler PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs( propertyName));
    }

    public bool ContainsKey(string key)
    {
        return ServerData.ContainsKey(key);
    }



    public void Apply(IDictionary<string, IParseFieldOperation> operationSet)
    {
        foreach (var pair in operationSet)
        {
            try
            {
                ServerData.TryGetValue(pair.Key, out var oldValue);
                var newValue = pair.Value.Apply(oldValue, pair.Key);
                if (newValue != ParseDeleteOperation.Token)
                    ServerData[pair.Key] = newValue;
                else
                    ServerData.Remove(pair.Key);
            }
            catch
            {
                // Log and skip incompatible field updates
                Debug.WriteLine($"Skipped incompatible operation for key: {pair.Key}");
            }
        }
    }

    public void Apply(IObjectState other)
    {
        IsNew = other.IsNew;

        if (other.ObjectId != null)
            ObjectId = other.ObjectId;
        if (other.UpdatedAt != null)
            UpdatedAt = other.UpdatedAt;
        if (other.CreatedAt != null)
            CreatedAt = other.CreatedAt;

        foreach (var pair in other)
        {
            try
            {
                ServerData[pair.Key] = pair.Value;
            }
            catch
            {
                // Log and skip incompatible fields
                Debug.WriteLine($"Skipped incompatible field: {pair.Key}");
            }
        }
    }
    public IObjectState MutatedClone(Action<MutableObjectState> func)
    {
        var clone = MutableClone();
        try
        {
            // Apply the mutation function to the clone
            func(clone);
        }
        catch (Exception ex)
        {
            // Log the failure and continue
            Debug.WriteLine($"Skipped incompatible mutation during clone: {ex.Message}");
        }
        return clone;
    }
    protected virtual MutableObjectState MutableClone()
    {
        return new MutableObjectState
        {
            IsNew = IsNew,
            ClassName = ClassName,
            ObjectId = ObjectId,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            
            ServerData = ServerData.ToDictionary(entry => entry.Key, entry => entry.Value)
        };
    }

    IEnumerator<KeyValuePair<string, object>> IEnumerable<KeyValuePair<string, object>>.GetEnumerator()
    {
        return ServerData.GetEnumerator();
    }

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
    {
        return ServerData.GetEnumerator();
    }

    public static MutableObjectState? Decode(object data)
    {
        if (data is IDictionary<string, object> dictionary)
        {
            try
            {
                var state = new MutableObjectState
                {
                    ClassName = dictionary.ContainsKey("className") ? dictionary["className"]?.ToString() : null,
                    ObjectId = dictionary.ContainsKey("objectId") ? dictionary["objectId"]?.ToString() : null,
                    CreatedAt = dictionary.ContainsKey("createdAt") ? DecodeDateTime(dictionary["createdAt"]) : null,
                    UpdatedAt = dictionary.ContainsKey("updatedAt") ? DecodeDateTime(dictionary["updatedAt"]) : null,
                    IsNew = dictionary.ContainsKey("isNew") && Convert.ToBoolean(dictionary["isNew"]),
                    ServerData = dictionary
                        .Where(pair => IsValidField(pair.Key, pair.Value))
                        .ToDictionary(pair => pair.Key, pair => pair.Value)
                };

                return state;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to decode MutableObjectState: {ex.Message}");
                throw new OperationCanceledException(ex.Message);

            }
        }

        return null;
    }

    private static DateTime? DecodeDateTime(object? value)
    {
        if (value is null)
            return null;
        if (value is DateTime dateTime)
            return dateTime;
        if (value is DateTimeOffset dto)
            return dto.UtcDateTime;

        string str = value.ToString()!;
        if (string.IsNullOrWhiteSpace(str))
            return null;

        // 1. Try ISO-8601 UTC Parse format
        var parsedIso = ParseDataDecoder.ParseDate(str);
        if (parsedIso.HasValue)
            return parsedIso;

        // 2. Fallback to standard DateTime parsers
        if (DateTime.TryParse(str, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var dt))
        {
            return dt;
        }
        if (DateTime.TryParse(str, out var dtLocal))
        {
            return dtLocal;
        }

        return null;
    }

    private static bool IsValidField(string key, object value)
    {
        // Add any validation logic for fields if needed
        return !string.IsNullOrEmpty(key); // Example: Ignore null/empty keys
    }
}
