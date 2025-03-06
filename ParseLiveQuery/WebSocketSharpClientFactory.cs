using System;
using System.IO;
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

    // Reconnection
    private int _reconnectionAttempts = 0;
    private const int MaxReconnectionAttempts = 5;
    private const int InitialReconnectDelayMs = 1000;
    private const int MaxReconnectDelayMs = 30000;

    // Ping-Pong
    private readonly TimeSpan _pingInterval = TimeSpan.FromSeconds(30);
    private CancellationTokenSource _pingCts;
    private const int PingTimeoutMs = 20000; // Timeout for receiving a pong

    // Buffer Size
    private int _receiveBufferSize = 8192; // Initial buffer size.

    private readonly Subject<WebSocketState> _stateChanges = new();
    private readonly Subject<string> _messages = new();
    private readonly Subject<Exception> _errors = new();
    private bool _disposed = false;

    public WebSocketClient(Uri hostUri, IWebSocketClientCallback webSocketClientCallback)
    {
        _hostUri = hostUri;
        _webSocketClientCallback = webSocketClientCallback;
        //_webSocket = new ClientWebSocket();
    }
    public WebSocketClient(Uri hostUri, IWebSocketClientCallback webSocketClientCallback, int receiveBufferSize)
    : this(hostUri, webSocketClientCallback)
    {
        _receiveBufferSize = receiveBufferSize;
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
        _webSocket = new ClientWebSocket(); // New WebSocket instance per connection attempt
        _stateChanges.OnNext(WebSocketState.Connecting);
        await ConnectWithRetryAsync();
    }
    private async Task ConnectWithRetryAsync()
    {
        while (!_cancellationTokenSource.Token.IsCancellationRequested && _reconnectionAttempts < MaxReconnectionAttempts)
        {
            try
            {
                await _webSocket.ConnectAsync(_hostUri, _cancellationTokenSource.Token);
                _stateChanges.OnNext(WebSocketState.Open);
                await _webSocketClientCallback.OnOpen();
                _reconnectionAttempts = 0; // Reset on successful connection
                _ = ReceiveLoopAsync(); // Start receiving messages
                StartPingPong();       // Start the ping/pong mechanism
                return; // Exit the loop on successful connection
            }
            catch (Exception ex)
            {
                _stateChanges.OnNext(WebSocketState.Error);
                _errors.OnNext(ex);
                // Do NOT call _webSocketClientCallback.OnError here; we're retrying.

                _reconnectionAttempts++;
                int delayMs = (int)Math.Min(InitialReconnectDelayMs * Math.Pow(2, _reconnectionAttempts - 1), MaxReconnectDelayMs);
                await Task.Delay(delayMs, _cancellationTokenSource.Token); // Exponential backoff
            }
        }

        if (_reconnectionAttempts >= MaxReconnectionAttempts)
        {
            // Notify of failure *after* all retries.
            _webSocketClientCallback.OnError(new Exception("Max reconnection attempts reached."));
        }
    }

    public async Task Close()
    {
        await CloseAsync(WebSocketCloseStatus.NormalClosure, "Client initiated close");
    }

    // Added CloseAsync method to allow specifying close status and description.
    public async Task CloseAsync(WebSocketCloseStatus closeStatus, string statusDescription)
    {
        if (_disposed)
        { return; }

        _cancellationTokenSource?.Cancel(); // Cancel any ongoing operations
        StopPingPong();                   // Stop the ping-pong loop

        if (_webSocket != null && (_webSocket.State == System.Net.WebSockets.WebSocketState.Open || _webSocket.State == System.Net.WebSockets.WebSocketState.CloseReceived || _webSocket.State == System.Net.WebSockets.WebSocketState.CloseSent))
        {
            try
            {
                // Use provided close status and description
                await _webSocket.CloseAsync(closeStatus, statusDescription, CancellationToken.None);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Exception when closing async "+ ex.Message);
                // Avoid exception.
            }
        }

        _stateChanges.OnNext(WebSocketState.Closed);
        _webSocketClientCallback.OnClose(); // Always notify the callback.
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
        MemoryStream outputStream = null;
        WebSocketReceiveResult receiveResult = null;
        var buffer = new byte[_receiveBufferSize]; // Use the configured buffer size

        while (_webSocket.State == System.Net.WebSockets.WebSocketState.Open && !_cancellationTokenSource.Token.IsCancellationRequested && !_disposed)
        {
            try
            {
                outputStream = new MemoryStream(_receiveBufferSize);
                do
                {
                    receiveResult = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), _cancellationTokenSource.Token);

                    if (receiveResult.MessageType != WebSocketMessageType.Close && receiveResult.MessageType != WebSocketMessageType.Close)
                    {
                        outputStream.Write(buffer, 0, receiveResult.Count);
                    }


                } while (!receiveResult.EndOfMessage && !_cancellationTokenSource.Token.IsCancellationRequested);


                if (receiveResult.MessageType == WebSocketMessageType.Close)
                {
                    // Extract close status and description.
                    _stateChanges.OnNext(WebSocketState.Closed);
                    _webSocketClientCallback.OnClose();
                    break;
                }
                else if (receiveResult.MessageType == WebSocketMessageType.Close)
                {
                    // Received a pong;  handled by SendPingAsync.
                    continue; // Go back to waiting for messages
                }
                else if (receiveResult.MessageType == WebSocketMessageType.Text)
                {
                    outputStream.Position = 0; // Reset position to read from the beginning
                    var message = Encoding.UTF8.GetString(outputStream.ToArray());
                    _messages.OnNext(message);
                    await _webSocketClientCallback.OnMessage(message);
                }
                else if (receiveResult.MessageType == WebSocketMessageType.Binary)
                {
                    // Handle binary messages
                    outputStream.Position = 0; // Reset position to read from the beginning
                    byte[] binaryData = outputStream.ToArray(); // Get the binary data
                                                                // You could add a new callback for binary data, e.g.
                                                                // _webSocketClientCallback.OnBinaryMessage(binaryData);
                                                                // Or convert to a string and use OnMessage (if appropriate).
                }

            }
            catch (WebSocketException ex) when (ex.WebSocketErrorCode != WebSocketError.ConnectionClosedPrematurely)
            {
                // Handle specific WebSocket errors, but don't exit for premature close.
                if (!_cancellationTokenSource.IsCancellationRequested && !_disposed)
                {
                    _errors.OnNext(ex);
                    _webSocketClientCallback.OnError(ex);
                    _stateChanges.OnNext(WebSocketState.Error);
                }
                break;
            }
            catch (Exception ex)
            {
                // Handle other exceptions.
                if (!_cancellationTokenSource.IsCancellationRequested && !_disposed)
                {
                    _errors.OnNext(ex);
                    _webSocketClientCallback.OnError(ex);
                    _stateChanges.OnNext(WebSocketState.Error);

                }
                break;
            }
            finally
            {
                outputStream?.Dispose(); // Dispose the stream after each message
            }

            // Reconnect if the connection was lost
            if (receiveResult == null || _webSocket.State != System.Net.WebSockets.WebSocketState.Open)
            {
                StopPingPong(); // Stop ping-pong before reconnecting
                _webSocket.Dispose(); // Dispose the old WebSocket
                _webSocket = new ClientWebSocket(); // Create a new WebSocket for reconnection
                await ConnectWithRetryAsync(); // Attempt to reconnect
            }
        }
    }


    private async Task SendPingAsync()
    {
        var pingTimeoutCts = new CancellationTokenSource(); // For ping timeout

        while (!_pingCts.Token.IsCancellationRequested && _webSocket.State == System.Net.WebSockets.WebSocketState.Open)
        {
            try
            {
                // Send a ping frame
                await _webSocket.SendAsync(new ArraySegment<byte>(Array.Empty<byte>()), WebSocketMessageType.Text, true, _pingCts.Token);

                // Reset the timeout cancellation token source for each new ping
                pingTimeoutCts.CancelAfter(PingTimeoutMs);

                // Wait for the ping interval or until timeout
                await Task.Delay(_pingInterval, _pingCts.Token);
            }
            catch (OperationCanceledException)
            {
                // Expected when _pingCts is canceled.  Exit gracefully.
                break;
            }
            catch (Exception ex)
            {
                _errors.OnNext(ex);
                break; // Exit on any other error
            }
        }
        pingTimeoutCts.Dispose(); // Always dispose
    }



    private void StartPingPong()
    {
        _pingCts = new CancellationTokenSource();
        _ = SendPingAsync(); // Fire and forget the ping loop
    }

    private void StopPingPong()
    {
        _pingCts?.Cancel();
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
            StopPingPong();
            _pingCts?.Dispose();
            _webSocket?.Dispose();
            _messages.OnCompleted();
            _errors.OnCompleted();
            _stateChanges.OnCompleted();
        }
        _disposed = true;
    }
}