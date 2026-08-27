using Parse.Abstractions.Internal;
using Parse.Infrastructure.Utilities;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace Parse.Platform.Objects;

internal class ParseObjectClass
{
    private readonly Func<ParseObject>? _fastActivator;
    public ParseObjectClass(Type type, ConstructorInfo constructor)
    {
        TypeInfo = type.GetTypeInfo();
        DeclaredName = TypeInfo.GetParseClassName();
        Constructor = constructor;

        var parameters = constructor.GetParameters();
        if (parameters.Length == 0)
        {
            _fastActivator = Expression.Lambda<Func<ParseObject>>(Expression.New(constructor)).Compile();
        }

        PropertyMappings = type.GetProperties()
            .Select(property => (Property: property, FieldNameAttribute: property.GetCustomAttribute<ParseFieldNameAttribute>(true)))
            .Where(set => set.FieldNameAttribute is { })
            .ToDictionary(set => set.Property.Name, set => set.FieldNameAttribute!.FieldName);
    }

    public TypeInfo TypeInfo { get; }

    public string DeclaredName { get; }

    public IDictionary<string, string>? PropertyMappings { get; }

    public ParseObject? Instantiate()
    {

        if (_fastActivator != null)
        {
            return _fastActivator();
        }

        // Fallback for 2-parameter constructor
        string className = DeclaredName ?? TypeInfo.Name;
        return Constructor?.Invoke(new object[] { className, ParseClient.Instance.Services }) as ParseObject;
    }
    ConstructorInfo? Constructor { get; }
}
