using Parse;
using Parse.Infrastructure.Control;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace YB.Parse.LiveQuery.Parse.Infrastructure.Utilities;

/// <summary>
/// A list wrapper that automatically synchronizes .Add(), .Remove(), and .Clear()
/// directly with the parent ParseObject's operation queue and dirty state tracking.
/// </summary>
public class ParseTrackedList<T> : IList<T>
{
    private readonly IList<T> _innerList;
    private readonly ParseObject _parent;
    private readonly string _fieldName;

    public ParseTrackedList(ParseObject parent, string fieldName, IList<T>? initialData)
    {
        _parent = parent;
        _fieldName = fieldName;
        _innerList = initialData ?? new List<T>();
    }

    public void Add(T item)
    {
        _innerList.Add(item);
        // Automatically registers an atomic ParseAddOperation!
        _parent.PerformOperation(_fieldName, new ParseAddOperation(new object?[] { item }));
    }

    public bool Remove(T item)
    {
        bool removed = _innerList.Remove(item);
        if (removed)
        {
            // Automatically registers an atomic ParseRemoveOperation!
            _parent.PerformOperation(_fieldName, new ParseRemoveOperation(new object[] { item }));
        }
        return removed;
    }

    public void Clear()
    {
        _innerList.Clear();
        // Replace with empty set
        _parent.PerformOperation(_fieldName, new ParseSetOperation(new List<object>()));
    }

    public int Count => _innerList.Count;
    public bool IsReadOnly => _innerList.IsReadOnly;
    public bool Contains(T item) => _innerList.Contains(item);
    public void CopyTo(T[] array, int arrayIndex) => _innerList.CopyTo(array, arrayIndex);
    public int IndexOf(T item) => _innerList.IndexOf(item);

    public void Insert(int index, T item)
    {
        _innerList.Insert(index, item);
        _parent.PerformOperation(_fieldName, new ParseSetOperation(_innerList));
    }

    public void RemoveAt(int index)
    {
        if (index >= 0 && index < _innerList.Count)
        {
            var item = _innerList[index];
            _innerList.RemoveAt(index);
            _parent.PerformOperation(_fieldName, new ParseRemoveOperation(new object[] { item }));
        }
    }

    public T this[int index]
    {
        get => _innerList[index];
        set
        {
            _innerList[index] = value;
            _parent.PerformOperation(_fieldName, new ParseSetOperation(_innerList));
        }
    }

    public IEnumerator<T> GetEnumerator() => _innerList.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _innerList.GetEnumerator();
}