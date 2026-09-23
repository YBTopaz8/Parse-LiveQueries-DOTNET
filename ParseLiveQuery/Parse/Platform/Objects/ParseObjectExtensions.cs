using Parse.Abstractions.Infrastructure;
using Parse.Abstractions.Internal;
using Parse.Infrastructure.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace Parse;

public static class ParseObjectExtensions
{
    // -----------------------------------------------------------
    // List Operations
    // -----------------------------------------------------------

    public static void AddToList<T, TProp, TItem>(this T obj, Expression<Func<T, TProp>> keySelector, TItem value)
        where T : ParseObject
        where TProp : IEnumerable<TItem>
    {
        obj.AddToList(ExpressionHelper.GetParseFieldName(keySelector), value);
    }

    public static void AddUniqueToList<T, TProp, TItem>(this T obj, Expression<Func<T, TProp>> keySelector, TItem value)
        where T : ParseObject
        where TProp : IEnumerable<TItem>
    {
        obj.AddUniqueToList(ExpressionHelper.GetParseFieldName(keySelector), value);
    }

    public static void AddRangeToList<T, TProp, TItem>(this T obj, Expression<Func<T, TProp>> keySelector, IEnumerable<TItem> values)
        where T : ParseObject
        where TProp : IEnumerable<TItem>
    {
        obj.AddRangeToList(ExpressionHelper.GetParseFieldName(keySelector), values);
    }

    public static void AddRangeUniqueToList<T, TProp, TItem>(this T obj, Expression<Func<T, TProp>> keySelector, IEnumerable<TItem> values)
        where T : ParseObject
        where TProp : IEnumerable<TItem>
    {
        obj.AddRangeUniqueToList(ExpressionHelper.GetParseFieldName(keySelector), values);
    }

    public static void RemoveAllFromList<T, TProp, TItem>(this T obj, Expression<Func<T, TProp>> keySelector, IEnumerable<TItem> values)
        where T : ParseObject
        where TProp : IEnumerable<TItem>
    {
        obj.RemoveAllFromList(ExpressionHelper.GetParseFieldName(keySelector), values);
    }

    // -----------------------------------------------------------
    // Increment Operations
    // -----------------------------------------------------------

    public static void Increment<T, TProp>(this T obj, Expression<Func<T, TProp>> keySelector)
        where T : ParseObject
    {
        obj.Increment(ExpressionHelper.GetParseFieldName(keySelector));
    }

    public static void Increment<T, TProp>(this T obj, Expression<Func<T, TProp>> keySelector, long amount)
        where T : ParseObject
    {
        obj.Increment(ExpressionHelper.GetParseFieldName(keySelector), amount);
    }

    public static void Increment<T, TProp>(this T obj, Expression<Func<T, TProp>> keySelector, double amount)
        where T : ParseObject
    {
        obj.Increment(ExpressionHelper.GetParseFieldName(keySelector), amount);
    }

    // -----------------------------------------------------------
    // Key Operations
    // -----------------------------------------------------------

    public static void Remove<T, TProp>(this T obj, Expression<Func<T, TProp>> keySelector)
        where T : ParseObject
    {
        obj.Remove(ExpressionHelper.GetParseFieldName(keySelector));
    }

    public static bool ContainsKey<T, TProp>(this T obj, Expression<Func<T, TProp>> keySelector)
        where T : ParseObject
    {
        return obj.ContainsKey(ExpressionHelper.GetParseFieldName(keySelector));
    }

    public static bool IsKeyDirty<T, TProp>(this T obj, Expression<Func<T, TProp>> keySelector)
        where T : ParseObject
    {
        return obj.IsKeyDirty(ExpressionHelper.GetParseFieldName(keySelector));
    }

    // ==============================================================================================
    // 1. SINGLE OBJECT FETCHING (Strongly-Typed)
    // ==============================================================================================

    /// <summary>
    /// Fetches this object with the data from the server, while simultaneously including nested relational ParseObjects.
    /// </summary>
    public static Task<T> FetchWithIncludeAsync<T>(
        this T obj,
        params Expression<Func<T, object>>[] propertySelectors) where T : ParseObject
    {
        return obj.FetchWithIncludeAsync(CancellationToken.None, propertySelectors);
    }

    /// <summary>
    /// Fetches this object with the data from the server, while simultaneously including nested relational ParseObjects.
    /// </summary>
    public static async Task<T> FetchWithIncludeAsync<T>(
        this T obj,
        CancellationToken cancellationToken,
        params Expression<Func<T, object>>[] propertySelectors) where T : ParseObject
    {
        var keys = propertySelectors.Select(ExpressionHelper.GetParseFieldName).ToArray();

        // Calls the existing string-based FetchWithIncludeAsync inside ParseObject
        var fetchedObj = await obj.FetchWithIncludeAsync(cancellationToken, keys).ConfigureAwait(false);

        return (T)fetchedObj;
    }

    // ==============================================================================================
    // 2. MULTIPLE OBJECTS FETCHING (Strongly-Typed)
    // ==============================================================================================

    /// <summary>
    /// Fetches all of the objects in the provided list, including the specified relational ParseObjects.
    /// </summary>
    public static Task<IEnumerable<T>> FetchObjectsAsync<T>(
        this IServiceHub serviceHub,
        IEnumerable<T> objects,
        params Expression<Func<T, object>>[] propertySelectors) where T : ParseObject
    {
        return serviceHub.FetchObjectsAsync(objects, CancellationToken.None, propertySelectors);
    }

    /// <summary>
    /// Fetches all of the objects in the provided list, including the specified relational ParseObjects.
    /// </summary>
    public static async Task<IEnumerable<T>> FetchObjectsAsync<T>(
        this IServiceHub serviceHub,
        IEnumerable<T> objects,
        CancellationToken cancellationToken,
        params Expression<Func<T, object>>[] propertySelectors) where T : ParseObject
    {
        var objList = objects.ToList();
        if (objList.Count == 0)
            return objList;

        // If no properties to include were passed, fallback to the standard internal fetch
        if (propertySelectors == null || propertySelectors.Length == 0)
        {
            return await serviceHub.FetchObjectsAsync(objList, cancellationToken).ConfigureAwait(false);
        }

        var keys = propertySelectors.Select(ExpressionHelper.GetParseFieldName).ToArray();
        await FetchListWithIncludesInternalAsync(serviceHub, objList, keys, cancellationToken).ConfigureAwait(false);

        return objList;
    }

    // ==============================================================================================
    // 3. MULTIPLE OBJECTS FETCH IF NEEDED (Strongly-Typed)
    // ==============================================================================================

    /// <summary>
    /// Fetches all of the objects that don't have data in the provided list, including the specified relational ParseObjects.
    /// </summary>
    public static Task<IEnumerable<T>> FetchObjectsIfNeededAsync<T>(
        this IServiceHub serviceHub,
        IEnumerable<T> objects,
        params Expression<Func<T, object>>[] propertySelectors) where T : ParseObject
    {
        return serviceHub.FetchObjectsIfNeededAsync(objects, CancellationToken.None, propertySelectors);
    }

    /// <summary>
    /// Fetches all of the objects that don't have data in the provided list, including the specified relational ParseObjects.
    /// </summary>
    public static async Task<IEnumerable<T>> FetchObjectsIfNeededAsync<T>(
        this IServiceHub serviceHub,
        IEnumerable<T> objects,
        CancellationToken cancellationToken,
        params Expression<Func<T, object>>[] propertySelectors) where T : ParseObject
    {
        var objList = objects.ToList();
        if (objList.Count == 0)
            return objList;

        // Find only the objects that actually need fetching
        var unfetchedObjects = objList.Where(o => !o.IsDataAvailable).ToList();
        if (unfetchedObjects.Count == 0)
            return objList;

        // If no selectors, fallback to standard fetch if needed
        if (propertySelectors == null || propertySelectors.Length == 0)
        {
            await serviceHub.FetchObjectsIfNeededAsync(unfetchedObjects, cancellationToken).ConfigureAwait(false);
            return objList;
        }

        var keys = propertySelectors.Select(ExpressionHelper.GetParseFieldName).ToArray();
        await FetchListWithIncludesInternalAsync(serviceHub, unfetchedObjects, keys, cancellationToken).ConfigureAwait(false);

        return objList;
    }

    // ==============================================================================================
    // INTERNAL QUERY LOGIC
    // ==============================================================================================

    private static async Task FetchListWithIncludesInternalAsync<T>(
        IServiceHub serviceHub,
        List<T> objectsToFetch,
        string[] includeKeys,
        CancellationToken cancellationToken) where T : ParseObject
    {
        if (objectsToFetch.Count == 0)
            return;

        var className = objectsToFetch.First().ClassName;
        var objectIds = objectsToFetch.Select(o => o.ObjectId).Where(id => id != null).Distinct().ToList();

        if (objectIds.Count == 0)
            throw new InvalidOperationException("Cannot fetch objects that haven't been saved to the server.");

        // Convert the batch fetch into a standard Query with Include()
        var query = new ParseQuery<T>(serviceHub, className).WhereContainedIn("objectId", objectIds);
        foreach (var key in includeKeys)
        {
            query = query.Include(key);
        }

        var results = await query.FindAsync(cancellationToken).ConfigureAwait(false);
        var resultMap = results.ToDictionary(r => r.ObjectId);

        // Merge the rich query results back into the original local instances
        foreach (var obj in objectsToFetch)
        {
            if (obj.ObjectId != null && resultMap.TryGetValue(obj.ObjectId, out var fetchedObj))
            {
                obj.MergeFromObject(fetchedObj);

                // Since ParseObject.MergeFromObject() does not set the Fetched boolean internally,
                // we must ensure it is marked as fetched.
                obj.Fetched = true;
            }
        }
    }
}
