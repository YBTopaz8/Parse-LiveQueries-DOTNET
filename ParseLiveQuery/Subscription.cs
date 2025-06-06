﻿// <Summary>
// This code defines a reactive subscription system for Parse Live Queries, enabling real-time updates
// from a Parse Server. It leverages System.Reactive for observable streams, allowing developers to
// easily subscribe to and react to events like object creation, updates, deletion, etc.  The system
// is designed to handle subscriptions, unsubscriptions (both immediate and delayed), and error
// handling in a clean and efficient manner.  The core classes are `Subscription<T>` (for typed
// ParseObject subscriptions) and `Subscription` (the abstract base class), along with helper
// classes and extension methods.
// </Summary>


using System;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using System.Threading;
using Parse.Abstractions.Platform.Objects;


namespace Parse.LiveQuery;

/// <summary>
/// Represents a typed subscription to a Parse Live Query.  This class handles events specific to
/// a ParseQuery of type T, where T is a ParseObject.
/// </summary>
/// <typeparam name="T">The type of ParseObject this subscription is for.</typeparam>
public class Subscription<T> : Subscription where T : ParseObject
{
    // Reactive Subjects for event streams.  Subjects are both observers and observables.
    private readonly Subject<SubscriptionEvent<T>> _eventStream = new();
    private readonly Subject<LiveQueryException> _errorStream = new();
    private readonly Subject<ParseQuery<T>> _subscribeStream = new();
    private readonly Subject<ParseQuery<T>> _unsubscribeStream = new();


    /// <summary>
    /// Initializes a new instance of the <see cref="Subscription{T}"/> class.
    /// </summary>
    /// <param name="requestId">The unique identifier for this subscription request.</param>
    /// <param name="query">The ParseQuery this subscription is based on.</param>
    /// <param name="unsubscribeAction">An action to be executed when this subscription is unsubscribed.</param>
    internal Subscription(int requestId, ParseQuery<T> query, Action<Subscription> unsubscribeAction) : base(requestId, query, unsubscribeAction)
    {
        RequestID = requestId;
        QueryObj = query;
    }

    // Observable streams for LINQ usage.  These provide a fluent interface for working with events.
    /// <summary>
    /// Gets an observable stream of subscription events (Create, Update, Delete, etc.).
    /// </summary>
    public IObservable<SubscriptionEvent<T>> Events => _eventStream.AsQbservable();
    /// <summary>
    /// Gets an observable stream of errors that occur during the subscription.
    /// </summary>
    public IObservable<LiveQueryException> Errors => _errorStream.AsQbservable();
    /// <summary>
    /// Gets an observable stream that emits the ParseQuery when the subscription is successfully established.
    /// </summary>
    public IObservable<ParseQuery<T>> Subscribes => _subscribeStream.AsQbservable();
    /// <summary>
    /// Gets an observable stream that emits the ParseQuery when the subscription is terminated.
    /// </summary>
    public IObservable<ParseQuery<T>> Unsubscribes => _unsubscribeStream.AsQbservable();

    /// <summary>
    /// internal Name of subscription (optional)
    /// </summary>
    internal override string Name { get; set; }


    /// <summary>
    /// Handles an incoming event from the Live Query server.
    /// </summary>
    /// <param name="queryObj">The query object associated with the event.</param>
    /// <param name="objEvent">The type of event (Create, Update, etc.).</param>
    /// <param name="objState">The state of the ParseObject involved in the event.</param>

    internal override void DidReceive(object queryObj, Event objEvent, IObjectState objState)
    {
        var query = (ParseQuery<T>)queryObj;
        var obj = ParseClient.Instance.CreateObjectWithoutData<T>(objState.ClassName ?? typeof(T).Name);
        obj.HandleFetchResult(objState);

        // Publish to the events stream
        _eventStream.OnNext(new SubscriptionEvent<T>(query, objEvent, obj));
    }
    /// <summary>
    /// Handles an error encountered by the Live Query subscription.
    /// </summary>
    /// <param name="queryObj">The query object associated with the error.</param>
    /// <param name="error">The exception that occurred.</param>
    internal override void DidEncounter(object queryObj, LiveQueryException error)
    {
        // Publish to the error stream
        _errorStream.OnNext(error);
    }
    /// <summary>
    /// Handles the successful subscription event.
    /// </summary>
    /// <param name="queryObj">The query object associated with the subscription.</param>
    internal override void DidSubscribe(object queryObj)
    {
        isConnected = true;
        // Publish to the subscribe stream
        _subscribeStream.OnNext((ParseQuery<T>)queryObj);
    }

    /// <summary>
    /// Handles the unsubscription event.
    /// </summary>
    /// <param name="queryObj">The query object associated with the unsubscription.</param>
    internal override void DidUnsubscribe(object queryObj)
    {
        isConnected = false;
        // Publish to the unsubscribe stream
        _unsubscribeStream.OnNext((ParseQuery<T>)queryObj);
    }
    /// <summary>
    /// Creates a client operation for subscribing to the Live Query.
    /// </summary>
    /// <param name="sessionToken">The session token to use for the subscription.</param>
    /// <returns>An IClientOperation representing the subscribe operation.</returns>
    internal override IClientOperation CreateSubscribeClientOperation(string sessionToken)
    {
        
        return new SubscribeClientOperation<T>(this, sessionToken);
    }


    /// <summary>
    /// Disposes of the resources used by the Subscription.
    /// </summary>
    /// <param name="disposing">True if called from Dispose, false if called from a finalizer.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _eventStream.OnCompleted();
            _errorStream.OnCompleted();
            _subscribeStream.OnCompleted();
            _unsubscribeStream.OnCompleted();

            _eventStream.Dispose();
            _errorStream.Dispose();
            _subscribeStream.Dispose();
            _unsubscribeStream.Dispose();
        }


    }
}

/// <summary>
/// Encapsulates a subscription event, providing the query, event type, and the affected ParseObject.
/// </summary>
/// <typeparam name="T">The type of ParseObject associated with the event.</typeparam>

public class SubscriptionEvent<T> where T : ParseObject
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SubscriptionEvent{T}"/> class.
    /// </summary>
    /// <param name="query">The ParseQuery associated with the event.</param>
    /// <param name="objEvent">The type of event.</param>
    /// <param name="obj">The ParseObject involved in the event.</param>
    public SubscriptionEvent(ParseQuery<T> query, Subscription.Event objEvent, T obj)
    {
        Query = query;
        EventType = objEvent;
        Object = obj;
    }

    /// <summary>
    /// Gets the ParseQuery associated with the event.
    /// </summary>
    public ParseQuery<T> Query { get; }
    /// <summary>
    /// Gets the type of event (Create, Update, etc.).
    /// </summary>
    public Subscription.Event EventType { get; }
    /// <summary>
    /// Gets the ParseObject involved in the event.
    /// </summary>
    public T Object { get; }
}


/// <summary>
/// Abstract base class for Live Query subscriptions.  Provides common functionality for
/// managing subscriptions and unsubscriptions.
/// </summary>
public abstract class Subscription : IDisposable
{

    private CancellationTokenSource _unsubscribeCts;
    private readonly Action<Subscription> _unsubscribeAction;
    /// <summary>
    /// Indicates whether the subscription is currently connected.
    /// </summary>
    protected bool isConnected = false;

    /// <summary>
    /// Gets a value indicating whether the subscription is currently connected.
    /// </summary>
    public bool IsConnected => isConnected;
    /// <summary>
    /// The Query of the subscription. This can now be any type of object,
    /// as it's used for communication and casted appropriately in subclasses.
    /// </summary>
    internal protected object QueryObj { get; set; }
    /// <summary>
    /// The request ID
    /// </summary>
    internal protected int RequestID { get; set; }
    /// <summary>
    /// internal Name of subscription (optional)
    /// </summary>
    internal virtual string Name { get; set; }

    /// <summary>
    /// Abstract method for handling received events.
    /// </summary>
    /// <param name="queryObj"></param>
    /// <param name="objEvent"></param>
    /// <param name="obj"></param>
    internal abstract void DidReceive(object queryObj, Event objEvent, IObjectState objState);

    /// <summary>
    /// Abstract method for handling errors.
    /// </summary>
    /// <param name="queryObj"></param>
    /// <param name="error"></param>
    internal abstract void DidEncounter(object queryObj, LiveQueryException error);
    /// <summary>
    /// Abstract method for handling DidSubscribe
    /// </summary>
    /// <param name="queryObj"></param>
    internal abstract void DidSubscribe(object queryObj);
    /// <summary>
    /// Abstract method for handling DidUnsubscribe
    /// </summary>
    /// <param name="queryObj"></param>
    internal abstract void DidUnsubscribe(object queryObj);

    /// <summary>
    /// Abstract method for creation of IClientOperation for subscription
    /// </summary>
    /// <param name="sessionToken"></param>
    /// <returns></returns>


    internal abstract IClientOperation CreateSubscribeClientOperation(string sessionToken);

    /// <summary>
    /// Initializes a new instance of the <see cref="Subscription"/> class.
    /// </summary>
    /// <param name="requestId">The unique identifier for this subscription request.</param>
    /// <param name="query">The query object this subscription is based on.</param>
    /// <param name="unsubscribeAction">An action to be executed when this subscription is unsubscribed.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="unsubscribeAction"/> is null.</exception>
    protected Subscription(int requestId, object query, Action<Subscription> unsubscribeAction)
    {
        RequestID = requestId;
        QueryObj = query;
        _unsubscribeAction = unsubscribeAction ?? throw new ArgumentNullException(nameof(unsubscribeAction));
        isConnected = false; // Initialize as not connected.
    }
    // Internal unsubscription method, called by extension methods
    internal void UnsubscribeInternal()
    {
        _unsubscribeCts?.Cancel(); // Cancel any delayed unsubscription
        _unsubscribeAction(this); // Execute the callback to inform the client
        DidUnsubscribe(QueryObj); // Notify the subscription itself
    }
    // Internal method for delayed unsubscription
    internal void UnsubscribeAfterInternal(long timeInMinutes)
    {
        isConnected = false; // Initialize as not connected.

        if (timeInMinutes <= 0)
        {
            UnsubscribeInternal();
            return;
        }
        _unsubscribeCts.Dispose();
        _unsubscribeCts = new CancellationTokenSource();
        Task.Delay(TimeSpan.FromMinutes(timeInMinutes), _unsubscribeCts.Token)
            .ContinueWith(t =>
            {
                if (!t.IsCanceled)
                {
                    UnsubscribeInternal();
                }
            }, TaskContinuationOptions.OnlyOnRanToCompletion);
    }
    /// <summary>
    /// Enumeration of possible Live Query events.
    /// </summary>
    public enum Event
    {
        /// <summary>
        /// connection opened
        /// </summary>
        Open,
        /// <summary>
        /// connection closed
        /// </summary>
        Close,
        /// <summary>
        /// error
        /// </summary>
        Error,
        /// <summary>
        /// object created
        /// </summary>
        Create,
        /// <summary>
        /// object entered query
        /// </summary>
        Enter,
        /// <summary>
        /// object updated
        /// </summary>
        Update,
        /// <summary>
        /// object left query
        /// </summary>
        Leave,
        /// <summary>
        /// object deleted
        /// </summary>
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

/// <summary>
/// Provides extension methods for the Subscription class to simplify unsubscription.
/// </summary>
public static class SubscriptionExtensions
{
    /// <summary>
    /// Unsubscribes from the Live Query immediately.
    /// </summary>
    /// <param name="subscription">The subscription to unsubscribe from.</param>
    public static void UnsubscribeNow(this Subscription subscription)
    {
        subscription.UnsubscribeInternal();
    }

    /// <summary>
    /// Unsubscribes from the Live Query after a specified delay.
    /// </summary>
    /// <param name="subscription">The subscription to unsubscribe from.</param>
    /// <param name="timeInMinutes">The delay, in minutes, before unsubscribing.</param>
    public static void UnsubscribeAfter(this Subscription subscription, long timeInMinutes)
    {
        subscription.UnsubscribeAfterInternal(timeInMinutes);
    }
}