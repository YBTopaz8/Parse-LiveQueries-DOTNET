using System;
using System.Net.WebSockets;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Parse.LiveQuery;

public class WebSocketClient : IWebSocketClient, IDisposable
{
    public static readonly WebSocketClientFactory Factory = (hostUri, callback) => new WebSocketClient(hostUri, callback);

    private readonly Uri _hostUri;
    private readonly IWebSocketClientCallback _webSocketClientCallback;
    private ClientWebSocket _webSocket;
    private CancellationTokenSource _cancellationTokenSource;


    private readonly Subject<WebSocketState> _stateChanges = new();
    private readonly Subject<string> _messages = new();
    private readonly Subject<Exception> _errors = new();
    private bool _disposed = false;

    public WebSocketClient(Uri hostUri, IWebSocketClientCallback webSocketClientCallback)
    {
        _hostUri = hostUri;
        _webSocketClientCallback = webSocketClientCallback;
        _webSocket = new ClientWebSocket();
    }

    public IObservable<WebSocketState> StateChanges => _stateChanges.AsObservable();
    public IObservable<string> Messages => _messages.AsObservable();
    public IObservable<Exception> Errors => _errors.AsObservable();


    public WebSocketState State
    {
        get
        {
            if (_webSocket == null)
            {
                return WebSocketState.None;
            }

            return _webSocket.State switch
            {
                System.Net.WebSockets.WebSocketState.None => WebSocketState.None,
                System.Net.WebSockets.WebSocketState.Connecting => WebSocketState.Connecting,
                System.Net.WebSockets.WebSocketState.Open => WebSocketState.Open,
                System.Net.WebSockets.WebSocketState.CloseReceived or System.Net.WebSockets.WebSocketState.CloseSent or System.Net.WebSockets.WebSocketState.Closed => WebSocketState.Closed,
                System.Net.WebSockets.WebSocketState.Aborted => WebSocketState.Error,
                _ => WebSocketState.None
            };
        }
    }


    public async Task Open()
    {
        
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(WebSocketClient));
        }
        _cancellationTokenSource = new CancellationTokenSource();
        _webSocket = new ClientWebSocket(); // Create a new websocket instance

        _stateChanges.OnNext(WebSocketState.Connecting);

        try
        {
            await _webSocket.ConnectAsync(_hostUri, _cancellationTokenSource.Token);
            _stateChanges.OnNext(WebSocketState.Open);
            await _webSocketClientCallback.OnOpen();

            _ = ReceiveLoopAsync();
        }
        catch (Exception ex)
        {
            _stateChanges.OnNext(WebSocketState.Error);
            _errors.OnNext(ex);
            _webSocketClientCallback.OnError(ex);
        }
    }

    public async Task Close()
    {
        if (_disposed)
        {
            return;
        }
        try
        {
            _cancellationTokenSource?.Cancel();
            if (_webSocket.State == System.Net.WebSockets.WebSocketState.Open)
            {
                await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed by user", CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            _errors.OnNext(ex);
        }
        finally
        {
            _stateChanges.OnNext(WebSocketState.Closed);
            _webSocketClientCallback.OnClose();
        }
    }

    public async Task Send(string message)
    {
        if (_disposed)
        {
            return; // Do not send anything when disposed
        }

        if (_webSocket.State != System.Net.WebSockets.WebSocketState.Open)
        {
            var exception = new InvalidOperationException("WebSocket is not connected.");
            _errors.OnNext(exception);
            _webSocketClientCallback.OnError(exception);
            return;
        }

        try
        {
            var buffer = Encoding.UTF8.GetBytes(message);
            var segment = new ArraySegment<byte>(buffer);
            await _webSocket.SendAsync(segment, WebSocketMessageType.Text, true, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _errors.OnNext(ex);
            _webSocketClientCallback.OnError(ex);
        }
    }

    private async Task ReceiveLoopAsync()
    {
        var buffer = new byte[8192];

        while (_webSocket.State == System.Net.WebSockets.WebSocketState.Open && !_cancellationTokenSource.Token.IsCancellationRequested && !_disposed)
        {
            try
            {
                var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), _cancellationTokenSource.Token);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _stateChanges.OnNext(WebSocketState.Closed);
                    _webSocketClientCallback.OnClose();
                    break;
                }

                var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                _messages.OnNext(message);
                await _webSocketClientCallback.OnMessage(message);

            }
            catch (Exception ex)
            {
                if (!_cancellationTokenSource.IsCancellationRequested && !_disposed)
                {
                    _errors.OnNext(ex);
                    _webSocketClientCallback.OnError(ex);
                    _stateChanges.OnNext(WebSocketState.Error);
                }
                break;
            }
        }
    }

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
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            _webSocket?.Dispose();
            _messages.OnCompleted();
            _errors.OnCompleted();
            _stateChanges.OnCompleted();
        }
        _disposed = true;
    }
}