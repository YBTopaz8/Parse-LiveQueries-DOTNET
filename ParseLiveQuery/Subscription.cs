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
        RequestId = requestId;
        Query = query;
    }

    public int RequestId { get; }

    internal ParseQuery<T> Query { get; }

    internal override object QueryObj => Query;
    //public string Name { get; set; } // Nullable string for optional name

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

public abstract class Subscription
{
    internal abstract object QueryObj { get; }
    internal abstract string Name { get; set; }

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


}
