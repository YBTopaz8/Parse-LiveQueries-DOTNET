using System;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using System.Threading;
using Parse.Abstractions.Platform.Objects;
using Parse.Infrastructure.Utilities;
//using Parse.Core.Internal;

namespace Parse.LiveQuery;

public class Subscription<T> : Subscription where T : ParseObject
{
    private readonly Subject<SubscriptionEvent<T>> _eventStream = new();
    private readonly Subject<LiveQueryException> _errorStream = new();
    private readonly Subject<ParseQuery<T>> _subscribeStream = new();
    private readonly Subject<ParseQuery<T>> _unsubscribeStream = new();

    internal Subscription(int requestId, ParseQuery<T> query)
    {
        RequestID = requestId;
        QueryObj = query;
    }
    
    // Observable streams for LINQ usage
    public IQbservable<SubscriptionEvent<T>> Events => _eventStream.AsQbservable();
    public IQbservable<LiveQueryException> Errors => _errorStream.AsQbservable();
    public IQbservable<ParseQuery<T>> Subscribes => _subscribeStream.AsQbservable();
    public IQbservable<ParseQuery<T>> Unsubscribes => _unsubscribeStream.AsQbservable();

    internal override string Name { get; set; }

    internal override void DidReceive(object queryObj, Event objEvent, IObjectState objState)
    {
        var query = (ParseQuery<T>)queryObj;
        var obj = ParseClient.Instance.CreateObjectWithoutData<T>(objState.ClassName ?? typeof(T).Name);
        obj.HandleFetchResult(objState);

        // Publish to the events stream
        _eventStream.OnNext(new SubscriptionEvent<T>(query, objEvent, obj));
    }

    internal override void DidEncounter(object queryObj, LiveQueryException error)
    {
        // Publish to the error stream
        _errorStream.OnNext(error);
    }

    internal override void DidSubscribe(object queryObj)
    {
        // Publish to the subscribe stream
        _subscribeStream.OnNext((ParseQuery<T>)queryObj);
    }

    internal override void DidUnsubscribe(object queryObj)
    {
        // Publish to the unsubscribe stream
        _unsubscribeStream.OnNext((ParseQuery<T>)queryObj);
    }

    internal override IClientOperation CreateSubscribeClientOperation(string sessionToken = null)
    {
        
        return new SubscribeClientOperation<T>(this, sessionToken);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            
            _eventStream.Dispose();
            _errorStream.Dispose();
            _subscribeStream.Dispose();
            _unsubscribeStream.Dispose();
        }
        
        base.Dispose(disposing);

    }
}

// Event class to encapsulate subscription events
public class SubscriptionEvent<T> where T : ParseObject
{
    public SubscriptionEvent(ParseQuery<T> query, Subscription.Event objEvent, T obj)
    {
        Query = query;
        EventType = objEvent;
        Object = obj;
    }

    public ParseQuery<T> Query { get; }
    public Subscription.Event EventType { get; }
    public T Object { get; }
}

public abstract class Subscription : IDisposable
{
    
    internal protected object QueryObj { get; set; }
    internal protected int RequestID { get; set; }
    internal virtual string Name { get; set; } 

    internal abstract void DidReceive(object queryObj, Event objEvent, IObjectState obj);

    internal abstract void DidEncounter(object queryObj, LiveQueryException error);

    internal abstract void DidSubscribe(object queryObj);

    internal abstract void DidUnsubscribe(object queryObj);

    internal abstract IClientOperation CreateSubscribeClientOperation(string sessionToken);

    public enum Event
    {
        Create,
        Enter,
        Update,
        Leave,
        Delete
    }

    // Dispose Pattern
    private bool _disposed = false;
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // No managed resources to dispose in the base class directly.
                // Derived classes will handle their own Subjects.
            }

            _disposed = true;
        }
    }
}
