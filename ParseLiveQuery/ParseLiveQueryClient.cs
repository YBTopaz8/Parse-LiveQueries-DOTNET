using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Parse.Abstractions.Platform.Objects;
using Parse.Infrastructure.Utilities;
using System.Diagnostics;
using YB.Parse.LiveQuery;
using System.IO;
using System.Reflection;
using System.Linq;
using System.Collections;

namespace Parse.LiveQuery;
public class ParseLiveQueryClient :IDisposable
{
    private bool _disposed;

    private readonly Uri _hostUri;
    private readonly string _applicationId;
    private readonly string _clientKey;
    private readonly WebSocketClientFactory _webSocketClientFactory;
    private readonly ITaskQueue _taskQueue;
    private readonly ISubscriptionFactory _subscriptionFactory;

    private IWebSocketClient _webSocketClient;
    private int _requestIdCount = 1;
    private bool _userInitiatedDisconnect;
    private bool _hasReceivedConnected;

    private readonly ConcurrentDictionary<string, Subscription> _namedSubscriptions = new();
    public IReadOnlyDictionary<string, Subscription> NamedSubscriptions => _namedSubscriptions;
    // Use ConcurrentDictionary for thread-safe operations on subscriptions
    private readonly ConcurrentDictionary<int, Subscription> _subscriptions = new();
    public IReadOnlyDictionary<int, Subscription> Subscriptions => _subscriptions;

    // Use Subjects for all event handling in Rx
    private readonly Subject<ParseLiveQueryClient> _connectedSubject = new();
    private readonly Subject<(ParseLiveQueryClient client, bool userInitiated)> _disconnectedSubject = new();
    private readonly Subject<LiveQueryException> _errorSubject = new();
    private readonly Subject<(int requestId, Subscription subscription)> _subscribedSubject = new();
    private readonly Subject<(int requestId, Subscription subscription)> _unsubscribedSubject = new();
    private readonly Subject<(Subscription.Event evt, object objectData, Subscription subscription)> _objectEventSubject = new();

    // Expose IObservables for consumption

    private readonly Subject<LiveQueryConnectionState> _connectionStateSubject = new();
    public IObservable<LiveQueryConnectionState> OnConnectionStateChanged => _connectionStateSubject.AsObservable();
    public IObservable<ParseLiveQueryClient> OnConnected => _connectedSubject.AsObservable();
    public IObservable<(ParseLiveQueryClient client, bool userInitiated)> OnDisconnected => _disconnectedSubject.AsObservable();
    public IObservable<LiveQueryException> OnError => _errorSubject.AsObservable();
    public IObservable<(int requestId, Subscription subscription)> OnSubscribed => _subscribedSubject.AsObservable();
    public IObservable<(int requestId, Subscription subscription)> OnUnsubscribed => _unsubscribedSubject.AsObservable();
    public IObservable<(Subscription.Event evt, object objectData, Subscription subscription)> OnObjectEvent => _objectEventSubject.AsObservable();




    public ParseLiveQueryClient() : this(GetDefaultUri()) { }
    public ParseLiveQueryClient(Uri hostUri) : this(hostUri, WebSocketClient.Factory) { }
    public ParseLiveQueryClient(WebSocketClientFactory webSocketClientFactory) : this(GetDefaultUri(), webSocketClientFactory) { }
    public ParseLiveQueryClient(Uri hostUri, WebSocketClientFactory webSocketClientFactory) :
        this(hostUri, webSocketClientFactory, new SubscriptionFactory(), new TaskQueueWrapper())
    { }

    internal ParseLiveQueryClient(Uri hostUri, WebSocketClientFactory webSocketClientFactory,
        ISubscriptionFactory subscriptionFactory, ITaskQueue taskQueue)
    {
        _hostUri = hostUri;
        _applicationId = ParseClient.Instance.ServerConnectionData.ApplicationID;
        _clientKey = ParseClient.Instance.ServerConnectionData.Key;

        _webSocketClientFactory = webSocketClientFactory;
        _subscriptionFactory = subscriptionFactory;
        _taskQueue = taskQueue;
    }

    // In ParseLiveQueryClient
    private LiveQueryConnectionState _connectionState = LiveQueryConnectionState.Disconnected;
    public LiveQueryConnectionState ConnectionState
    {
        get => _connectionState;
        private set // Only the class can change the state.
        {
            if (_connectionState != value)
            {
                _connectionState = value;
                _connectionStateSubject.OnNext(_connectionState);
            }
        }
    }


    private static Uri GetDefaultUri()
    {
        string server = ParseClient.Instance.ServerConnectionData.ServerURI ?? throw new InvalidOperationException("Missing default Server URI in CurrentConfiguration");

        Uri serverUri = new(server);
        return new UriBuilder(serverUri)
        {
            Scheme = serverUri.Scheme.Equals("https") ? "wss" : "ws"
        }.Uri;
    }

    /// <summary>
    /// Gets a subscription by its client-side name. Returns null if not found.
    /// If multiple subscriptions have the same name (overwrite strategy), returns the latest one.
    /// </summary>
    public Subscription GetSubscriptionByName(string subscriptionName)
    {
        _namedSubscriptions.TryGetValue(subscriptionName, out var subscription);
        return subscription; // Will be null if not found
    }

    /// <summary>
    /// Gets all subscriptions that have the specified client-side name.
    /// In the current implementation (overwrite strategy), this will return a list with at most one subscription.
    /// </summary>
    public List<Subscription> GetSubscriptionsByName(string subscriptionName)
    {
        if (_namedSubscriptions.TryGetValue(subscriptionName, out var subscription))
        {
            return new List<Subscription> { subscription };
        }
        return new List<Subscription>(); // Return empty list if not found
    }

    /// <summary>
    /// Subscribes to a specified Parse query to receive real-time updates 
    /// for Create, Update, Delete, and other events on objects matching the query.
    /// </summary>
    public Subscription<T> Subscribe<T>(ParseQuery<T> query, string SubscriptionName=null) where T : ParseObject
    {
        Action<Subscription> unsubscribeAction = (subscription) =>
        {
            if (_subscriptions.TryRemove(subscription.RequestID, out var removedSubscription))
            {
                if (!string.IsNullOrEmpty(removedSubscription.Name))
                {
                    _namedSubscriptions.TryRemove(removedSubscription.Name, out _);
                }
            }
        };

        // Create Subscription object
        var requestId = _requestIdCount++;
        var subscription = _subscriptionFactory.CreateSubscription(requestId, query, unsubscribeAction);
        if (!string.IsNullOrEmpty(SubscriptionName))
        {
            subscription.Name = SubscriptionName;
            
            _namedSubscriptions
                .AddOrUpdate(SubscriptionName, subscription, 
                (name, oldSubscription) => subscription); // Overwrite if name exists
            
        }
        // Add to subscriptions collection
        _subscriptions.TryAdd(requestId, subscription);

        // Handle cases
        if (!IsConnected)
        {
            ConnectIfNeeded();
        }
        else if (_userInitiatedDisconnect)
        {
            throw new InvalidOperationException("The client was explicitly disconnected and must be reconnected before subscribing");
        }
        
        SendSubscription(subscription);

        return subscription;
    }


    public void ConnectIfNeeded()
    {
        switch (GetWebSocketState())
        {
            case WebSocketClientState.None:
            case WebSocketClientState.Disconnecting:
            case WebSocketClientState.Disconnected:
                Reconnect();
                break;
            case WebSocketClientState.Connecting:
            case WebSocketClientState.Connected:
                _hasReceivedConnected=true;
                break;
            default:
                break;
        }
    }

    public void RemoveAllSubscriptions()
    {
        ThrowIfDisposed();
        if (_subscriptions is null || _subscriptions.IsEmpty)
        {
            return;
        }

        // Capture a thread-safe snapshot of the subscriptions
        foreach (var pair in _subscriptions)
        {
            var requestId = pair.Key;
            var subscription = pair.Value;

            // Use the non-generic SendUnsubscription
            SendUnsubscription(subscription);

            if (_subscriptions.TryRemove(requestId, out _))
            {
                if (!string.IsNullOrEmpty(subscription.Name))
                {
                    _namedSubscriptions.TryRemove(subscription.Name, out _);
                }
            }
        }
    }

    public void Unsubscribe<T>(ParseQuery<T> query) where T : ParseObject
    {
        if (query == null)
            return;
        RemoveSubscriptions(query, null);
    }

    public void Unsubscribe<T>(ParseQuery<T> query, Subscription<T> subscription) where T : ParseObject
    {
        if (query == null || subscription == null)
            return;
        RemoveSubscriptions(query, subscription);
    }

    // Private method to centralize unsubscription logic
    private void RemoveSubscriptions<T>(ParseQuery<T> query, Subscription<T> specificSubscription) where T : ParseObject
    {
        ThrowIfDisposed();

        var subscriptionsToRemove = _subscriptions.Where(pair =>
            query.Equals(pair.Value.QueryObj) && (specificSubscription == null || specificSubscription.Equals(pair.Value))
        ).ToList();

        foreach (var pair in subscriptionsToRemove)
        {
            // Use the non-generic SendUnsubscription.
            SendUnsubscription(pair.Value);
            if (_subscriptions.TryRemove(pair.Key, out var removedSubscription))
            {
                if (!string.IsNullOrEmpty(removedSubscription.Name))
                {
                    _namedSubscriptions.TryRemove(removedSubscription.Name, out _);
                }
            }
        }
    }


    public void Reconnect()
    {
        _webSocketClient?.Close();
        _userInitiatedDisconnect = false;
        _hasReceivedConnected = false;
        _webSocketClient = _webSocketClientFactory(_hostUri, new WebSocketClientCallback(this)); // Pass callback here
        _webSocketClient.Open();
        if (_webSocketClient.State == WebSocketState.Open)
        {
            _hasReceivedConnected=true;
        }
    }


    public void Disconnect()
    {
        _webSocketClient?.Close();
        _webSocketClient = null;

        _userInitiatedDisconnect = true;
        _hasReceivedConnected = false;
        _disconnectedSubject.OnNext((this, _userInitiatedDisconnect));
    }


    private WebSocketClientState GetWebSocketState()
    {
        return _webSocketClient == null ? WebSocketClientState.None :
            _webSocketClient.State switch
            {
                WebSocketState.Connecting => WebSocketClientState.Connecting,
                WebSocketState.Open => WebSocketClientState.Connected,
                WebSocketState.Closed or WebSocketState.Error => WebSocketClientState.Disconnected,
                _ => WebSocketClientState.None
            };
    }

    public bool IsConnected { get => _hasReceivedConnected; }

    private void SendSubscription(Subscription subscription)
    {
        _taskQueue.EnqueueOnError(
            SendOperationWithSessionAsync(session => subscription.CreateSubscribeClientOperation(session ?? string.Empty)),
            error => subscription.DidEncounter(subscription.QueryObj, new LiveQueryException.UnknownException("Error when subscribing", error))
        );
    }


    private void SendUnsubscription(Subscription subscription)
    {
        SendOperationAsync(new UnsubscribeClientOperation(subscription.RequestID));
    }


    private Task SendOperationWithSessionAsync(Func<string, IClientOperation> operationFunc)
    {
        return _taskQueue.EnqueueOnSuccess(
       ParseClient.Instance.CurrentUserController.GetCurrentSessionTokenAsync(ParseClient.Instance.Services, CancellationToken.None),

       currentSessionTokenTask =>
       {
           var sessionToken = currentSessionTokenTask?.Result; // Can be null if no session
           return SendOperationAsync(operationFunc(sessionToken));
       }
   );
    }

    private Task SendOperationAsync(IClientOperation operation)
    {

        return _taskQueue.Enqueue(() => _webSocketClient.Send(operation.ToJson()));
    }

    private Task HandleOperationAsync(string message)
    {
        return _taskQueue.Enqueue(() => ParseMessage(message));
    }

    private Dictionary<string, object> ConvertJsonElements(Dictionary<string, JsonElement> jsonElementDict)
    {
        var result = new Dictionary<string, object>();

        foreach (var kvp in jsonElementDict)
        {
            JsonElement element = kvp.Value;

            object value = element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number => element.TryGetInt64(out long l) ? l : element.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => element
            };

            result[kvp.Key] = value;
        }

        return result;
    }

    private void ParseMessage(string message)
    {
        try
        {
            var jsonElementDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(message);

            if (jsonElementDict == null || !jsonElementDict.ContainsKey("op"))
            {
                throw new LiveQueryException.InvalidResponseException("Message does not contain a valid 'op' field.");
            }

            var jsonObject = ConvertJsonElements(jsonElementDict);
            string rawOperation = jsonObject["op"] as string;
            if (string.IsNullOrEmpty(rawOperation))
            {
                throw new LiveQueryException.InvalidResponseException("'op' field is null or empty.");
            }

            switch (rawOperation)
            {
                case "connected":
                    _hasReceivedConnected = true;
                    _connectedSubject.OnNext(this);
                    foreach (Subscription subscription in _subscriptions.Values)
                    {
                        SendSubscription(subscription);
                    }
                    break;
                case "redirect":
                    // TODO: Handle redirect if needed
                    break;
                case "subscribed":
                    HandleSubscribedEvent(jsonObject);
                    break;
                case "unsubscribed":
                    HandleUnsubscribedEvent(jsonObject);
                    break;
                case "enter":
                    HandleObjectEvent(Subscription.Event.Enter, jsonObject);
                    break;
                case "leave":
                    HandleObjectEvent(Subscription.Event.Leave, jsonObject);
                    break;
                case "update":
                    HandleObjectEvent(Subscription.Event.Update, jsonObject);
                    break;
                case "create":
                    HandleObjectEvent(Subscription.Event.Create, jsonObject);
                    break;
                case "delete":
                    HandleObjectEvent(Subscription.Event.Delete, jsonObject);
                    break;
                case "error":
                    HandleErrorEvent(jsonObject);
                    break;
                default:
                    throw new LiveQueryException.InvalidResponseException($"Unexpected operation: {rawOperation}");
            }
        }
        catch (Exception e) when (e is not LiveQueryException)
        {
            _errorSubject.OnNext(new LiveQueryException.InvalidResponseException(message, e));
        }
    }

    private void HandleSubscribedEvent(IDictionary<string, object> jsonObject)
    {
        if (jsonObject.TryGetValue("requestId", out var requestIdObj) &&
            requestIdObj is int requestId &&
                _subscriptions.TryGetValue(requestId, out var subscription))
        {
            subscription.DidSubscribe(subscription.QueryObj);
            _subscribedSubject.OnNext((requestId, subscription));
        }
    }


    private void HandleUnsubscribedEvent(IDictionary<string, object> jsonObject)
    {
        if (jsonObject.TryGetValue("requestId", out var requestIdObj) 
            &&
            requestIdObj is int requestId 
            &&
            _subscriptions.TryRemove(requestId, out var subscription))
        {
            if (subscription != null && !string.IsNullOrEmpty(subscription.Name))
            {
                _namedSubscriptions.TryRemove(subscription.Name, out _);
            }
            subscription.DidUnsubscribe(subscription.QueryObj);
            _unsubscribedSubject.OnNext((requestId, subscription));
        }
    }


    private void HandleObjectEvent(Subscription.Event subscriptionEvent, IDictionary<string, object> jsonObject)
    {
        try
        {
            int requestId = Convert.ToInt32(jsonObject["requestId"]);
            var jsonElement = (JsonElement)jsonObject["object"];
            var objectData = JsonElementToDictionary(jsonElement);
            //var objectElement = (JsonElement)jsonObject["original"]; // TODO: Expose this in the future
            if (_subscriptions.TryGetValue(requestId, out var subscription))
            {
                var obj = ParseClient.Instance.Decoder.Decode(objectData, ParseClient.Instance.Services);

                _objectEventSubject.OnNext((subscriptionEvent, obj, subscription));
            }

        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error in HandleObjectEvent: {ex.Message}");
            _errorSubject.OnNext(new LiveQueryException.UnknownException("Error handling object event", ex));
        }
    }


    private IDictionary<string, object> JsonElementToDictionary(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("Expected JsonElement to be an object.");
        }

        var result = new Dictionary<string, object>();
        foreach (var property in element.EnumerateObject())
        {
            result[property.Name] = JsonElementToObject(property.Value);
        }
        return result;
    }

    private object JsonElementToObject(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => JsonElementToDictionary(element),
            JsonValueKind.Array => JsonArrayToObjectList(element),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out long l) ? l : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => throw new ArgumentException($"Unsupported JsonValueKind: {element.ValueKind}")
        };
    }


    private List<object> JsonArrayToObjectList(JsonElement element)
    {
        var list = new List<object>();
        foreach (var arrayElement in element.EnumerateArray())
        {
            list.Add(JsonElementToObject(arrayElement));
        }
        return list;
    }


    private void HandleErrorEvent(IDictionary<string, object> jsonObject)
    {
        if (jsonObject.TryGetValue("requestId", out var requestIdObj) && requestIdObj is int requestId)
        {

            if (_subscriptions.TryGetValue(requestId, out var subscription))
            {

                int code = Convert.ToInt32(jsonObject["code"]);
                string error = (string)jsonObject["error"];
                bool reconnect = (bool)jsonObject["reconnect"];
                LiveQueryException exception = new LiveQueryException.ServerReportedException(code, error, reconnect);
                subscription.DidEncounter(subscription.QueryObj, exception);
                _errorSubject.OnNext(exception);
            }
        }
        else
        {
            int code = Convert.ToInt32(jsonObject["code"]);
            string error = (string)jsonObject["error"];
            bool reconnect = (bool)jsonObject["reconnect"];
            LiveQueryException exception = new LiveQueryException.ServerReportedException(code, error, reconnect);
            _errorSubject.OnNext(exception);
        }
    }

    private void OnWebSocketClosed() => _disconnectedSubject.OnNext((this, _userInitiatedDisconnect));

    private void OnWebSocketError(Exception exception) => _errorSubject.OnNext(new LiveQueryException.UnknownException("Socket error", exception));

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            // Dispose managed resources (Rx Subjects, WebSocketClient)
            Disconnect();
            RemoveAllSubscriptions();

            _connectedSubject.Dispose();
            _disconnectedSubject.Dispose();
            _errorSubject.Dispose();
            _subscribedSubject.Dispose();
            _unsubscribedSubject.Dispose();
            _objectEventSubject.Dispose();

            // The WebSocketClient itself might not be IDisposable,
            // but if it has a Close() or similar method, call it here.
            _webSocketClient?.Close();

            _subscriptions.Clear();
            _namedSubscriptions.Clear();
        }

        // Dispose unmanaged resources (if any).  Likely none in this case.

        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ParseLiveQueryClient));
        }
    }
    private class WebSocketClientCallback : IWebSocketClientCallback
    {
        private readonly ParseLiveQueryClient _client;
        public WebSocketClientCallback(ParseLiveQueryClient client)
        {
            _client = client;
        }

        public async Task OnOpen()
        {
            try
            {
                _client._hasReceivedConnected = false;
                await _client.SendOperationWithSessionAsync(session =>
                    new ConnectClientOperation(_client._applicationId, _client._clientKey, session))
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                var exception = ex is AggregateException ae ? ae.Flatten() : ex;
                _client._errorSubject.OnNext(
                    exception is LiveQueryException lqex ? lqex :
                    new LiveQueryException.UnknownException("Error connecting client", exception));
            }
        }


        public async Task OnMessage(string message)
        {
            try
            {
                await _client.HandleOperationAsync(message).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                var exception = ex is AggregateException ae ? ae.Flatten() : ex;
                _client._errorSubject.OnNext(
                    exception is LiveQueryException lqex ? lqex :
                    new LiveQueryException.UnknownException($"Error handling message: {message}", exception));
            }
        }

        public void OnClose()
        {
            _client._hasReceivedConnected = false;
            _client.OnWebSocketClosed();
        }

        public void OnError(Exception exception)
        {
            _client._hasReceivedConnected = false;
            _client.OnWebSocketError(exception);
        }
        public void OnStateChanged()
        {
            // No callback needed
        }

    }
    private class SubscriptionFactory : ISubscriptionFactory
    {
        public Subscription<T> CreateSubscription<T>(int requestId, ParseQuery<T> query, Action<Subscription> unsubscribeAction) where T : ParseObject
        {
            return new Subscription<T>(requestId, query, unsubscribeAction);
        }
    }

        

}


public interface IWebSocketClientCallback
{
    Task OnOpen();
    Task OnMessage(string message);
    void OnClose();
    void OnError(Exception exception);
    void OnStateChanged();
}
public enum LiveQueryConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting,
    Failed // Or PermanentlyDisconnected
}



public static class ObjectMapper
{
    /// <summary>
    /// Maps values from a dictionary to an instance of type T.
    /// Logs any keys that don't match properties in T.
    ///     
    /// Helper to Map from Parse Dictionnary Response to Model
    /// Example usage TestChat chat = ObjectMapper.MapFromDictionary<TestChat>(objData);    
    /// </summary>
    public static T MapFromDictionary<T>(IDictionary<string, object> source) where T : class
    {
        try
        {
            // Attempt to create an instance of T using Activator.CreateInstance
            // This will return null if T doesn't have a parameterless constructor (or if it's abstract)
            T target = (T)Activator.CreateInstance(typeof(T));

            if (target == null)
            {
                Debug.WriteLine($"Warning: Could not create an instance of {typeof(T).Name}. Ensure it has a public parameterless constructor.");
                return null; // Return null if instantiation fails
            }

            // Get all writable properties of T
            var properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanWrite)
                .ToDictionary(p => p.Name, p => p, StringComparer.OrdinalIgnoreCase);

            // Track unmatched keys
            List<string> unmatchedKeys = new();

            foreach (var kvp in source)
            {
                if (properties.TryGetValue(kvp.Key, out var property))
                {
                    try
                    {
                        // Convert and assign the value to the property
                        if (kvp.Value != null && property.PropertyType.IsAssignableFrom(kvp.Value.GetType()))
                        {
                            property.SetValue(target, kvp.Value);
                        }
                        else if (kvp.Value != null)
                        {
                            // Attempt conversion for non-directly assignable types
                            var convertedValue = Convert.ChangeType(kvp.Value, property.PropertyType);
                            property.SetValue(target, convertedValue);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Failed to set property {property.Name} on {typeof(T).Name}: {ex.Message}");
                    }
                }
                else
                {
                    // Log unmatched keys
                    unmatchedKeys.Add(kvp.Key);
                }
            }

            // Log keys that don't match
            if (unmatchedKeys.Count > 0)
            {
                Debug.WriteLine($"Unmatched Keys for {typeof(T).Name}:");
                foreach (var key in unmatchedKeys)
                {
                    Debug.WriteLine($"- {key}");
                }
            }

            return target;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error creating instance of {typeof(T).Name}: {ex.Message}");
            return null; // Return null if any error occurs during instantiation
        }
    }

    public static Dictionary<string, object> ClassToDictionary<T>(T obj) where T : class
    {
        if (obj == null)
        {
            return null;
        }

        var dictionary = new Dictionary<string, object>();
        var properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var property in properties)
        {
            string name = property.Name;
            object value = property.GetValue(obj);

            if (value != null)
            {
                dictionary[name] = ConvertValueForDictionary(value);
            }
        }

        return dictionary;
    }

    private static object ConvertValueForDictionary(object value)
    {
        if (value == null)
        {
            return null;
        }

        // Handle Parse specific types first for efficiency

        // ParseObject (for Pointers)
        if (value is ParseObject parseObject && !string.IsNullOrEmpty(parseObject.ObjectId))
        {
            return new Dictionary<string, object>
            {
                { "__type", "Pointer" },
                { "className", parseObject.ClassName },
                { "objectId", parseObject.ObjectId }
            };
        }

        // ParseFile
        if (value is ParseFile parseFile && !string.IsNullOrEmpty(parseFile.Name) && parseFile.DataStream != null)
        {
            using var memoryStream = new MemoryStream();
            parseFile.DataStream.CopyTo(memoryStream);
            return new Dictionary<string, object>
            {
                { "__type", "File" },
                { "name", parseFile.Name },
                // URL is typically not set on a new ParseFile, so we rely on the data
                { "_ContentType", parseFile.MimeType }, // Optional, but good to include
                { "_Base64", Convert.ToBase64String(memoryStream.ToArray()) }
            };
        }

        // ParseGeoPoint
        if (value is ParseGeoPoint geoPoint)
        {
            return new Dictionary<string, object>
            {
                { "__type", "GeoPoint" },
                { "latitude", geoPoint.Latitude },
                { "longitude", geoPoint.Longitude }
            };
        }

        //// ParseRelation (Asynchronous)
        //if (value is ParseRelation<ParseObject> relation)
        //{
        //    var targetObjects = await relation.Query.FindAsync();
        //    return targetObjects.Select(obj => new Dictionary<string, object>
        //    {
        //        { "__type", "Pointer" },
        //        { "className", obj.ClassName },
        //        { "objectId", obj.ObjectId }
        //    }).ToList<object>();
        //}
        Type valueType = value.GetType();

        if (valueType.IsPrimitive || value is string)
        {
            return value;
        }

        if (value is DateTime dateTime)
        {
            return new Dictionary<string, object>
            {
                { "__type", "Date" },
                { "iso", dateTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ") }
            };
        }

        if (value is byte[] byteArray)
        {
            return new Dictionary<string, object>
            {
                { "__type", "Bytes" },
                { "base64", Convert.ToBase64String(byteArray) }
            };
        }

        if (value is Stream stream)
        {
            using var memoryStream = new MemoryStream();
            stream.CopyTo(memoryStream);
            return new Dictionary<string, object>
            {
                { "__type", "Bytes" },
                { "base64", Convert.ToBase64String(memoryStream.ToArray()) }
            };
        }

        if (value is IList list) // Handle generic IList
        {
            if (list is not null)
            {
                var convertedList = new List<object>();
                foreach (var item in list)
                {
                    convertedList.Add(ConvertValueForDictionary(item));
                }

                return convertedList;
            }
        }

        if (value is IDictionary dictionary)
        {
            var convertedDictionary = new Dictionary<string, object>();
            foreach (var key in dictionary.Keys)
            {
                convertedDictionary[key.ToString()] = ConvertValueForDictionary(dictionary[key]);
            }
            return convertedDictionary;
        }

        // Handle nested custom objects recursively
        if (value.GetType().IsClass && value.GetType() != typeof(string))
        {
            return ClassToDictionary(value);
        }

        return value?.ToString(); // Fallback for unhandled types
    }
    
}