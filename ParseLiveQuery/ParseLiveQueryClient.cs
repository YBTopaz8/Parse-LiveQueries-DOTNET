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
    
    private readonly ConcurrentDictionary<int, Subscription> _subscriptions = new();
    public IReadOnlyDictionary<int, Subscription> Subscriptions => _subscriptions;

    
    private readonly Subject<ParseLiveQueryClient> _connectedSubject = new();
    private readonly Subject<(ParseLiveQueryClient client, bool userInitiated)> _disconnectedSubject = new();
    private readonly Subject<LiveQueryException> _errorSubject = new();
    private readonly Subject<(int requestId, Subscription subscription)> _subscribedSubject = new();
    private readonly Subject<(int requestId, Subscription subscription)> _unsubscribedSubject = new();
    private readonly Subject<(Subscription.Event evt, ParseObject objectData, Subscription subscription)> _objectEventSubject = new();

    

    private readonly Subject<LiveQueryConnectionState> _connectionStateSubject = new();
    public IObservable<LiveQueryConnectionState> OnConnectionStateChanged => _connectionStateSubject.AsObservable();
    public IObservable<ParseLiveQueryClient> OnConnected => _connectedSubject.AsObservable();
    public IObservable<(ParseLiveQueryClient client, bool userInitiated)> OnDisconnected => _disconnectedSubject.AsObservable();
    public IObservable<LiveQueryException> OnError => _errorSubject.AsObservable();
    public IObservable<(int requestId, Subscription subscription)> OnSubscribed => _subscribedSubject.AsObservable();
    public IObservable<(int requestId, Subscription subscription)> OnUnsubscribed => _unsubscribedSubject.AsObservable();
    public IObservable<(Subscription.Event evt, ParseObject objectData, Subscription subscription)> OnObjectEvent => _objectEventSubject.AsObservable();




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

    
    private readonly LiveQueryConnectionState _connectionState = LiveQueryConnectionState.Disconnected;
    public LiveQueryConnectionState ConnectionState
    {
        get => _connectionState;
        
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
    public Subscription GetSubscriptionByName(string subscriptionName)
    {
        _namedSubscriptions.TryGetValue(subscriptionName, out var subscription);
        return subscription; 
    }

    public List<Subscription> GetSubscriptionsByName(string subscriptionName)
    {
        if (_namedSubscriptions.TryGetValue(subscriptionName, out var subscription))
        {
            return new List<Subscription> { subscription };
        }
        return Enumerable.Empty<Subscription>().ToList(); 
    }
    public async Task<Subscription<T>> SubscribeAsync<T>(ParseQuery<T> query, string SubscriptionName=null) where T : ParseObject
    {
        void unsubscribeAction(Subscription subscription)
        {
            if (_subscriptions.TryRemove(subscription.RequestID, out var removedSubscription)&&!string.IsNullOrEmpty(removedSubscription.Name))
            {
                _namedSubscriptions.TryRemove(removedSubscription.Name, out _);
            }
        }

        
        var requestId = _requestIdCount++;
        var subscription = _subscriptionFactory.CreateSubscription(requestId, query, unsubscribeAction);
        if (!string.IsNullOrEmpty(SubscriptionName))
        {
            subscription.Name = SubscriptionName;
            
            _namedSubscriptions
                .AddOrUpdate(SubscriptionName, subscription, 
                (name, oldSubscription) => subscription); 
            
        }
        
        _subscriptions.TryAdd(requestId, subscription);

        
        if (!IsConnected)
        {
            await ConnectIfNeededAsync();
        }
        else if (_userInitiatedDisconnect)
        {
            throw new InvalidOperationException("The client was explicitly disconnected and must be reconnected before subscribing");
        }
        
        await SendSubscriptionAsync(subscription);

        return subscription;
    }


    public async Task ConnectIfNeededAsync()
    {
        switch (GetWebSocketState())
        {
            case WebSocketClientState.None:
            case WebSocketClientState.Disconnecting:
            case WebSocketClientState.Disconnected:
                await ReconnectAsync();
                break;
            case WebSocketClientState.Connecting:
            case WebSocketClientState.Connected:
                _hasReceivedConnected=true;
                break;
            default:
                break;
        }
    }

    public async Task RemoveAllSubscriptions()
    {
        ThrowIfDisposed();
        if (_subscriptions is null || _subscriptions.IsEmpty)
        {
            return;
        }

        
        foreach (var pair in _subscriptions)
        {
            var requestId = pair.Key;
            var subscription = pair.Value;

            
            await SendUnsubscriptionAsync(subscription);

            if (_subscriptions.TryRemove(requestId, out _)&&!string.IsNullOrEmpty(subscription.Name))
            {
                _namedSubscriptions.TryRemove(subscription.Name, out _);
            }
        }
    }

    public async Task Unsubscribe<T>(ParseQuery<T> query) where T : ParseObject
    {
        if (query == null)
            return;
        await RemoveSubscriptions(query, null);
    }

    public async Task Unsubscribe<T>(ParseQuery<T> query, Subscription<T> subscription) where T : ParseObject
    {
        if (query == null || subscription == null)
            return;
        await RemoveSubscriptions(query, subscription);
    }

    
    private async Task RemoveSubscriptions<T>(ParseQuery<T> query, Subscription<T> specificSubscription) where T : ParseObject
    {
        ThrowIfDisposed();

        var subscriptionsToRemove = _subscriptions.Where(pair =>
            query.Equals(pair.Value.QueryObj) && (specificSubscription == null || specificSubscription.Equals(pair.Value))
        ).ToList();

        foreach (var pair in subscriptionsToRemove)
        {
            
            await SendUnsubscriptionAsync(pair.Value);
            if (_subscriptions.TryRemove(pair.Key, out var removedSubscription)&&!string.IsNullOrEmpty(removedSubscription.Name))
            {
                _namedSubscriptions.TryRemove(removedSubscription.Name, out _);
            }
        }
    }


    public async Task ReconnectAsync()
    {
        if(_webSocketClient!=null)
        {
            await _webSocketClient?.CloseAsync();
        }
        _userInitiatedDisconnect = false;
        _hasReceivedConnected = false;
        _webSocketClient = _webSocketClientFactory(_hostUri, new WebSocketClientCallback(this), 8094); 
        await _webSocketClient.OpenAsync();
        if (_webSocketClient.State == WebSocketState.Open)
        {
            _hasReceivedConnected=true;
        }
    }


    public async Task DisconnectAsync()
    {
        await _webSocketClient?.CloseAsync();
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

    private Task SendSubscriptionAsync(Subscription subscription)
    {
       return  _taskQueue.EnqueueOnError(
            SendOperationWithSessionAsync(session => subscription.CreateSubscribeClientOperation(session ?? string.Empty)),
            error => subscription.DidEncounter(subscription.QueryObj, new LiveQueryException.UnknownException("Error when subscribing", error))
        );
    }


    private async Task SendUnsubscriptionAsync(Subscription subscription)
    {
        await SendOperationAsync(new UnsubscribeClientOperation(subscription.RequestID));
    }


    private Task SendOperationWithSessionAsync(Func<string, IClientOperation> operationFunc)
    {
        return _taskQueue.EnqueueOnSuccess(
            ParseClient.Instance.CurrentUserController.GetCurrentSessionTokenAsync(ParseClient.Instance.Services, CancellationToken.None),
       currentSessionTokenTask =>
            {
                var sessionToken = currentSessionTokenTask?.Result; 
                return SendOperationAsync(operationFunc(sessionToken));
            });
    }

    private Task SendOperationAsync(IClientOperation operation)
    {
        return _taskQueue.Enqueue(async () => await _webSocketClient.SendAsync(operation.ToJson()));
    }

    private Task HandleOperationAsync(string message)
    {
        return _taskQueue.Enqueue(async () => await ParseMessage(message));
    }

    private static Dictionary<string, object> ConvertJsonElements(Dictionary<string, JsonElement> jsonElementDict)
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

    private async Task ParseMessage(string message)
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
                       await SendSubscriptionAsync(subscription);
                    }
                    break;
                case "redirect":
                    
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


    private void HandleObjectEvent(Subscription.Event subscriptionEvent, Dictionary<string, object> jsonObject)
    {
        try
        {
            int requestId = Convert.ToInt32(jsonObject["requestId"]);
            var jsonElement = (JsonElement)jsonObject["object"];
            var objectData = JsonElementToDictionary(jsonElement);

            if (_subscriptions.TryGetValue(requestId, out var subscription))
            {
                var obj = ParseClient.Instance.Decoder.Decode(objectData, ParseClient.Instance.Services) as ParseObject;

                _objectEventSubject.OnNext((subscriptionEvent, obj, subscription));
            }

        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error in HandleObjectEvent: {ex.Message}");
            _errorSubject.OnNext(new LiveQueryException.UnknownException("Error handling object event", ex));
        }
    }

    private void HandleSubscribedEvent(Dictionary<string, object> jsonObject)
    {
        if (jsonObject.TryGetValue("requestId", out var requestIdObj) &&
            requestIdObj is int requestId &&
                _subscriptions.TryGetValue(requestId, out var subscription))
        {
            subscription.DidSubscribe(subscription.QueryObj);
            _subscribedSubject.OnNext((requestId, subscription));
        }
    }


    private void HandleUnsubscribedEvent(Dictionary<string, object> jsonObject)
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


    private Dictionary<string, object> JsonElementToDictionary(JsonElement element)
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


    private void HandleErrorEvent(Dictionary<string, object> jsonObject)
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
        _= Task.Run(async () => await Dispose(true));
        GC.SuppressFinalize(this);
    }

    protected virtual async Task Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            
            await DisconnectAsync();
            await RemoveAllSubscriptions();

            _connectedSubject.Dispose();
            _disconnectedSubject.Dispose();
            _errorSubject.Dispose();
            _subscribedSubject.Dispose();
            _unsubscribedSubject.Dispose();
            _objectEventSubject.Dispose();

            
            
            await _webSocketClient?.CloseAsync();

            _subscriptions.Clear();
            _namedSubscriptions.Clear();
        }

        

        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ParseLiveQueryClient));
        }
    }
    sealed class WebSocketClientCallback : IWebSocketClientCallback
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
            
        }

    }
    sealed class SubscriptionFactory : ISubscriptionFactory
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
    Failed 
}

