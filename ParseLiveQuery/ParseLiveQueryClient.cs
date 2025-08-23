using Parse.Infrastructure;
using Parse.Infrastructure.Data;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.WebSockets;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using YB.Parse.LiveQuery;

namespace Parse.LiveQuery;
public class ParseLiveQueryClient :IAsyncDisposable
{

    private readonly ConcurrentQueue<IClientOperation> _operationQueue = new();

    private bool _disposed;

    private readonly Uri _hostUri;
    private readonly string _applicationId;
    private readonly string _clientKey;
    private readonly WebSocketClientFactory _webSocketClientFactory;
    private readonly ITaskQueue _taskQueue;
    private readonly ISubscriptionFactory _subscriptionFactory;

    private IWebSocketClient _webSocketClient;
    private int _requestIdCount = 0;

    private readonly ConcurrentDictionary<string, Subscription> _namedSubscriptions = new();
    public IReadOnlyDictionary<string, Subscription> NamedSubscriptions => _namedSubscriptions;
    
    private readonly ConcurrentDictionary<int, Subscription> _subscriptions = new();
    public IReadOnlyDictionary<int, Subscription> Subscriptions => _subscriptions;
    private readonly Subject<DisconnectInfo> _disconnectedSubject = new();
    public IObservable<DisconnectInfo> OnDisconnected => _disconnectedSubject.AsObservable();

    private readonly Subject<ParseLiveQueryClient> _connectedSubject = new();
    private readonly Subject<LiveQueryException> _errorSubject = new();
    private readonly Subject<(int requestId, Subscription subscription)> _subscribedSubject = new();
    private readonly Subject<(int requestId, Subscription subscription)> _unsubscribedSubject = new();


    private readonly Subject<LiveQueryConnectionState> _connectionStateSubject = new Subject<LiveQueryConnectionState>(); 
    public IObservable<LiveQueryConnectionState> OnConnectionStateChanged => _connectionStateSubject.AsObservable();
    public IObservable<ParseLiveQueryClient> OnConnected => _connectedSubject.AsObservable();
    public IObservable<LiveQueryException> OnError => _errorSubject.AsObservable();
    public IObservable<(int requestId, Subscription subscription)> OnSubscribed => _subscribedSubject.AsObservable();
    public IObservable<(int requestId, Subscription subscription)> OnUnsubscribed => _unsubscribedSubject.AsObservable();


    private ParseClient ParseClientInstance { get; set; }

    public ParseLiveQueryClient() : this(GetDefaultUri()) { }
    public ParseLiveQueryClient(ParseClient client) : this(GetDefaultUri()) 
    { 
    //ParseClientInstance
    }
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


    private readonly object _stateLock = new();
    private readonly LiveQueryConnectionState _connectionState = LiveQueryConnectionState.Disconnected;
    public LiveQueryConnectionState ConnectionState
    {
        get
        {
            lock (_stateLock)
            {
                return _connectionState;
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
    public Subscription<T> Subscribe<T>(ParseQuery<T> query, string SubscriptionName=null) where T : ParseObject
    {
        void unsubscribeAction(Subscription subscription)
        {
            if (_subscriptions.TryRemove(subscription.RequestID, out var removedSubscription)&&!string.IsNullOrEmpty(removedSubscription.Name))
            {
                _namedSubscriptions.TryRemove(removedSubscription.Name, out _);
            }
        }

        
        var requestId = Interlocked.Increment(ref _requestIdCount);
        var subscription = _subscriptionFactory.CreateSubscription(requestId, query, unsubscribeAction);
        if (!string.IsNullOrEmpty(SubscriptionName))
        {
            subscription.Name = SubscriptionName;
            
            _namedSubscriptions
                .AddOrUpdate(SubscriptionName, subscription, 
                (name, oldSubscription) => subscription); 
            
        }
        
        _subscriptions.TryAdd(requestId, subscription);

        Start();

      
        //await SendSubscriptionAsync(subscription);

        return subscription;
    }


    public void ConnectIfNeeded()
    {
        lock (_stateLock)
        {
            if (ConnectionState == LiveQueryConnectionState.Disconnected || ConnectionState == LiveQueryConnectionState.Failed)
            {
               _=  ReconnectAsync();
                return;
            }
            return ;
        }
    }
    private void OnWebSocketClosed(WebSocketCloseStatus? status, string description)
    {
        var userInitiated = (ConnectionState == LiveQueryConnectionState.Disconnected); 
        var info = new DisconnectInfo(this, userInitiated, status, description);
        _disconnectedSubject.OnNext(info);

        
        if (!userInitiated)
        {
            SetConnectionState(LiveQueryConnectionState.Reconnecting);
            _ = ReconnectAsync();
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
        
        
        _webSocketClient = _webSocketClientFactory(_hostUri, new WebSocketClientCallback(this), 8094); 
        await _webSocketClient.OpenAsync();
        if (_webSocketClient.State == WebSocketState.Open)
        {
            
        }
    }


    public async Task DisconnectAsync()
    {
        await _webSocketClient?.CloseAsync();
        _webSocketClient = null;

        
        
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
        // If we are connected, send immediately.
        if ( _webSocketClient?.State == WebSocketState.Open)
        {
            return _taskQueue.Enqueue(async () => await _webSocketClient.SendAsync(operation.ToJson()));
        }

        _operationQueue.Enqueue(operation);
        return Task.CompletedTask;
    }


    private async Task ProcessOperationQueueAsync()
    {
        if (_operationQueue.IsEmpty)
            return;

        
        while (_operationQueue.TryDequeue(out var operation))
        {
            try
            {
                // Send the queued operation. We add a small delay to avoid overwhelming the server on a fresh connection.
                await SendOperationAsync(operation);
                await Task.Delay(100); // Small buffer between queued messages
            }
            catch (ParseFailureException ex)
            {
              throw  new ParseFailureException(ex.Code,ex.Message,ex);
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message);  // Depending on desired robustness, you could re-queue the failed operation.
            }
        }
    }

    private Task HandleOperationAsync(string message)
    {
        return _taskQueue.Enqueue(async () => await ParseMessage(message));
    }
  
    private static object ConvertJsonNumber(JsonElement element)
    {
        if (element.TryGetInt32(out int i))
        {
            return i;
        }
        if (element.TryGetInt64(out long l))
        {
            return l;
        }
        double d = element.GetDouble();

        return element.GetDouble();
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
                JsonValueKind.Number => ConvertJsonNumber(element),
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
                    await _taskQueue.Enqueue(() =>
                    {
                        SetConnectionState(LiveQueryConnectionState.Connected);
                        _connectedSubject.OnNext(this);
                    });

                    var resubscribeTasks = _subscriptions.Values.Select(sub => SendSubscriptionAsync(sub));
                    await Task.WhenAll(resubscribeTasks);
                    await ProcessOperationQueueAsync();
                    break;
                case "subscribed":
                    await _taskQueue.Enqueue(() => HandleSubscribedEvent(jsonObject));
                    break;
                case "unsubscribed":
                    await _taskQueue.Enqueue(() => HandleUnsubscribedEvent(jsonObject));
                    break;
                case "enter":
                    await _taskQueue.Enqueue(() => HandleObjectEvent(Subscription.Event.Enter, jsonObject));
                    break;
                case "leave":
                    await _taskQueue.Enqueue(()=> HandleObjectEvent(Subscription.Event.Leave, jsonObject));
                    break;
                case "update":
                    
                    await _taskQueue.Enqueue(()=> HandleObjectEvent(Subscription.Event.Update, jsonObject));
                    break;
                case "create":
                    await _taskQueue.Enqueue(()=> HandleObjectEvent(Subscription.Event.Create, jsonObject));
                    break;
                case "delete":
                    await _taskQueue.Enqueue(()=> HandleObjectEvent(Subscription.Event.Delete, jsonObject));
                    break;
                case "error":
                    await _taskQueue.Enqueue(() => HandleErrorEvent(jsonObject));
                    if (!jsonObject.ContainsKey("requestId"))
                    {
                        SetConnectionState(LiveQueryConnectionState.Failed);
                    }
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
            


            if (_subscriptions.TryGetValue(requestId, out var subscription))
            {
                

                var jsonElement = (JsonElement)jsonObject["object"];
                var objectData = JsonElementToDictionary(jsonElement);



                var obj = ParseClient.Instance.Decoder.Decode(objectData, ParseClientInstance);


                if (obj != null)
                {
                    // Directly route the event to the correct subscription.
                    subscription.DidReceive(subscription.QueryObj, subscriptionEvent, obj as ParseObject);
                }
            }
        }
        catch (Exception ex)
        {
            
            _errorSubject.OnNext(new LiveQueryException.UnknownException("Error handling object event", ex));
        }
    }

    private void HandleSubscribedEvent(Dictionary<string, object> jsonObject)
    {
        var idd = jsonObject.TryGetValue("requestId", out var requestIdObj);
        var id = (int)requestIdObj;
        _subscriptions.TryGetValue(id , out var subscription);


            subscription.DidSubscribe(subscription.QueryObj);
            _subscribedSubject.OnNext((id, subscription));
        
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

    private void OnWebSocketError(Exception exception)
    {
        _taskQueue.Enqueue(() => _errorSubject.OnNext(new LiveQueryException.UnknownException("Socket error", exception)));
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

            if(_webSocketClient is not null)
            {

            
            await _webSocketClient?.CloseAsync();

            }
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
    internal class WebSocketClientCallback : IWebSocketClientCallback
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
                
                await _client.SendOperationWithSessionAsync(session =>
                    new ConnectClientOperation(_client._applicationId, _client._clientKey, session))
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _client.SetConnectionState(LiveQueryConnectionState.Failed);
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

        public void OnClose(WebSocketCloseStatus? status, string description)
        {
            
            _client.OnWebSocketClosed(status, description);
        }
        // --- ADD this new private method to ParseLiveQueryClient ---
        
        public void OnError(Exception exception)
        {
            
            _client.OnWebSocketError(exception);
        }
        public void OnStateChanged()
        {
            
        }

    }


    private void SetConnectionState(LiveQueryConnectionState state)
    {
        lock (_stateLock)
        {
            if (_connectionState == state)
                return;
            _connectionStateSubject.OnNext(state);
        }
    }

    internal class SubscriptionFactory : ISubscriptionFactory
    {
        public Subscription<T> CreateSubscription<T>(int requestId, ParseQuery<T> query, Action<Subscription> unsubscribeAction) where T : ParseObject
        {
            return new Subscription<T>(requestId, query, unsubscribeAction);
        }
    }

    /// <summary>
    /// Explicitly starts the client, initiates connection, and enables auto-reconnection.
    /// </summary>
    public void Start()
    {
        ThrowIfDisposed();
        if (_clientState == ClientState.Started)
            return;

        _clientState = ClientState.Started;
        
        ConnectIfNeeded(); // Start the connection loop
    }
    /// <summary>
    /// Explicitly stops the client, disconnects, and disables auto-reconnection.
    /// </summary>
    public async Task StopAsync()
    {
        ThrowIfDisposed();
        if (_clientState == ClientState.Stopped)
            return;

        _clientState = ClientState.Stopped;
        
        _operationQueue.Clear(); // Clear any pending operations
        await DisconnectAsync();
    }
   
    public async ValueTask DisposeAsync()
    {
        await Dispose(true);
        GC.SuppressFinalize(this);
    }

    private ClientState _clientState = ClientState.Stopped;
    private enum ClientState { Stopped, Started, Disposed }
}

public record DisconnectInfo(
    ParseLiveQueryClient Client,
    bool UserInitiated,
    WebSocketCloseStatus? CloseStatus,
    string Reason
);
public interface IWebSocketClientCallback
{
    Task OnOpen();
    Task OnMessage(string message);
    //void OnClose();
    void OnClose(WebSocketCloseStatus? status, string description);
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
