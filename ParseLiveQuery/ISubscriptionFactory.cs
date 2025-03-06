using System;

namespace Parse.LiveQuery;
public interface ISubscriptionFactory
{
    Subscription<T> CreateSubscription<T>(int requestId, ParseQuery<T> query, Action<Subscription> unsubscribeAction) where T : ParseObject;
}