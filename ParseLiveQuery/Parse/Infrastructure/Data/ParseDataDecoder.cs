using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Parse.Abstractions.Infrastructure;
using Parse.Abstractions.Infrastructure.Data;
using Parse.Abstractions.Platform.Objects;
using Parse.Infrastructure.Control;
using Parse.Infrastructure.Utilities;

namespace Parse.Infrastructure.Data;

public class ParseDataDecoder : IParseDataDecoder
{
    private IServiceHub Services { get; }
    private IParseObjectClassController ClassController => Services.ClassController;

    public ParseDataDecoder(IServiceHub serviceHub) => Services = serviceHub ?? throw new ArgumentNullException(nameof(serviceHub));

    static string[] Types { get; } = { "Date", "Bytes", "Pointer", "File", "GeoPoint", "Object", "Relation" };

    public object Decode(object data)
    {
            return data switch
            {
                null => default,
                IDictionary<string, object> { } dictionary when dictionary.ContainsKey("__op") => ParseFieldOperations.Decode(dictionary,this, ClassController),

                IDictionary<string, object> { } dictionary when dictionary.TryGetValue("__type", out var type) && Types.Contains(type) => type switch
                {
                    "Date" => ParseDate(dictionary.TryGetValue("iso", out var iso) ? iso as string : throw new KeyNotFoundException("Missing 'iso' for Date type")),

                    "Bytes" => Convert.FromBase64String(dictionary.TryGetValue("base64", out var base64) ? base64 as string : throw new KeyNotFoundException("Missing 'base64' for Bytes type")),

                    "Pointer" => DecodePointer(
                        dictionary.TryGetValue("className", out var className) ? className as string : throw new KeyNotFoundException("Missing 'className' for Pointer type"),
                        dictionary.TryGetValue("objectId", out var objectId) ? objectId as string : throw new KeyNotFoundException("Missing 'objectId' for Pointer type"),
                        this.Services),

                    "File" => new ParseFile(
                        dictionary.TryGetValue("name", out var name) ? name as string : throw new KeyNotFoundException("Missing 'name' for File type"),
                        new Uri(dictionary.TryGetValue("url", out var url) ? url as string : throw new KeyNotFoundException("Missing 'url' for File type"))),

                    "GeoPoint" => new ParseGeoPoint(
                        Conversion.To<double>(dictionary.TryGetValue("latitude", out var latitude) ? latitude : throw new KeyNotFoundException("Missing 'latitude' for GeoPoint type")),
                        Conversion.To<double>(dictionary.TryGetValue("longitude", out var longitude) ? longitude : throw new KeyNotFoundException("Missing 'longitude' for GeoPoint type"))),

                    "Object" => ClassController.GenerateObjectFromState<ParseObject>(
                        ParseObjectCoder.Instance.Decode(dictionary, this, this.Services),
                        dictionary.TryGetValue("className", out var objClassName) ? objClassName as string : throw new KeyNotFoundException("Missing 'className' for Object type"),
                         this.Services),

                    "Relation" => this.Services.CreateRelation(null, null, dictionary.TryGetValue("className", out var relClassName) ? relClassName as string : throw new KeyNotFoundException("Missing 'className' for Relation type")),
                    _ => throw new NotSupportedException($"Unsupported Parse type '{type}' encountered")
                },


                IDictionary<string, object> { } dictionary when dictionary.ContainsKey("className") =>
          ClassController.GenerateObjectFromState<ParseObject>(ParseObjectCoder.Instance.Decode(dictionary, this, this.Services), dictionary["className"] as string, this.Services),

                IDictionary<string, object> { } dictionary => dictionary.ToDictionary(pair => pair.Key, pair => Decode(pair.Value)),
                IList<object> { } list => list.Select(item => Decode(item)).ToList(),
                _ => data
            };
        
    }

    protected virtual object DecodePointer(string className, string objectId, IServiceHub  services) =>
        ClassController.CreateObjectWithoutData(className, objectId,  this.Services);

    public static DateTime? ParseDate(string input)
    {
        if (string.IsNullOrEmpty(input))
            return null;

        foreach (var format in ParseClient.DateFormatStrings)
        {
            if (DateTime.TryParseExact(input, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
            {
                return parsedDate;
            }
        }

        return null; // Return null if no formats match
    }

}
