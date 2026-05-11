using System;
using System.Buffers;
using System.Diagnostics;
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

    
    public static readonly WebSocketClientFactory Factory = (hostUri, callback,  bufferSize) =>
        new WebSocketClient(hostUri, callback,  bufferSize);

    private readonly Uri _hostUri;
    private readonly IWebSocketClientCallback _webSocketClientCallback;
    
    private ClientWebSocket _webSocket;
    private CancellationTokenSource _internalCts; 
    private readonly object _stateLock = new(); 

    
    private int _reconnectionAttempts = 0;
    private const int MaxReconnectionAttempts = 5;
    private const int InitialReconnectDelayMs = 1000;
    private const int MaxReconnectDelayMs = 30000;

    
    private readonly TimeSpan _keepAliveInterval = TimeSpan.FromSeconds(30); 
    private readonly int _receiveBufferSize; 

    
    private readonly Subject<WebSocketState> _stateChanges = new();
    private readonly Subject<string> _messages = new();
    private readonly Subject<ReadOnlyMemory<byte>> _binaryMessages = new(); 
    private readonly Subject<Exception> _errors = new();
    private volatile bool _disposed = false;
    private volatile WebSocketState _currentState = WebSocketState.None;
    
    public WebSocketClient(Uri hostUri, IWebSocketClientCallback webSocketClientCallback)
    {
        _hostUri = hostUri;
        _webSocketClientCallback = webSocketClientCallback;
        
    }
    public WebSocketClient(Uri hostUri, IWebSocketClientCallback webSocketClientCallback,
        
        int receiveBufferSize)
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

    public IObservable<ReadOnlyMemory<byte>> BinaryMessages => throw new NotImplementedException();
    


    public async Task OpenAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(WebSocketClient));

        

        CancellationTokenSource linkedCts = null;
        try
        {
            lock (_stateLock)
            {
                if (State is WebSocketState.Connecting or WebSocketState.Open)
                {
                    
                    return;
                }

                // Ensure previous CTS is cancelled and disposed if OpenAsync is called multiple times


#if NET8_0_OR_GREATER
                _internalCts?.CancelAsync();
#else
                _internalCts?.Cancel();
#endif
                _internalCts?.Dispose();
                _internalCts = new CancellationTokenSource();
                linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_internalCts.Token, cancellationToken);

                UpdateState(WebSocketState.Connecting);
                _reconnectionAttempts = 0; // Reset reconnection attempts on explicit open
            }

            // Start connection attempt outside lock
            await ConnectWithRetryAsync(linkedCts.Token);
        }
        catch (OperationCanceledException oCEx) when (cancellationToken.IsCancellationRequested)
        {
            
            // If cancellation came from the external token, ensure clean state
            await HandleConnectionClosedOrFailedAsync("Open cancelled by caller." +oCEx.Message);
        }
        catch (Exception ex)
        {
            
            ReportError(ex);
            await HandleConnectionClosedOrFailedAsync($"Open failed: {ex.Message}");
        }
        finally
        {
            linkedCts?.Dispose();
        }
    }


    public async Task SendAsync(string message, CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            
            return;
        }

        var currentWebSocket = _webSocket; 

        if (currentWebSocket == null || currentWebSocket.State != System.Net.WebSockets.WebSocketState.Open)
        {
            var state = currentWebSocket?.State.ToString() ?? "null";
            var exception = new InvalidOperationException($"WebSocket is not connected (State: {state}). Cannot send message.");
            
            ReportError(exception);
            
            return;
        }

        try
        {
            
                                                                     
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_internalCts?.Token ?? CancellationToken.None, cancellationToken);

            
            var buffer = Encoding.UTF8.GetBytes(message);
            await currentWebSocket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, linkedCts.Token);
            
        }
        catch (OperationCanceledException oCEx) when (cancellationToken.IsCancellationRequested || _internalCts?.IsCancellationRequested == true)
        {

            ReportError(oCEx);
        }
        catch (Exception ex)
        {
            
            ReportError(ex);
            
            _ = HandleConnectionClosedOrFailedAsync($"Send failed: {ex.Message}");
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken loopCancellationToken)
    {
        
        byte[] buffer = null;
        MemoryStream messageStream = null;

        try
        {
            buffer = ArrayPool<byte>.Shared.Rent(_receiveBufferSize);
            messageStream = new MemoryStream(); 

            while (_webSocket?.State == System.Net.WebSockets.WebSocketState.Open && !loopCancellationToken.IsCancellationRequested)
            {
                messageStream.SetLength(0); 
                messageStream.Position = 0;
                ValueWebSocketReceiveResult receiveResult;

                do
                {
                    
                    var bufferSegment = new Memory<byte>(buffer);
                    receiveResult = await _webSocket.ReceiveAsync(bufferSegment, loopCancellationToken);

                    if (loopCancellationToken.IsCancellationRequested)
                        break; 

                    
                    switch (receiveResult.MessageType)
                    {
                        case WebSocketMessageType.Text:
                        case WebSocketMessageType.Binary:
                            
                            await messageStream.WriteAsync(bufferSegment.Slice(0, receiveResult.Count), loopCancellationToken);
                            break;

                        case WebSocketMessageType.Close:

                            var closeStatus = _webSocket.CloseStatus;
                            var statusDescription = _webSocket.CloseStatusDescription;
                            await HandleConnectionClosedOrFailedAsync("Server initiated close.", closeStatus, statusDescription); // Pass details
                            return;

                        default:
                            
                            
                            break;
                    }

                } while (!receiveResult.EndOfMessage && !loopCancellationToken.IsCancellationRequested);

                if (loopCancellationToken.IsCancellationRequested)
                    break; 

                
                messageStream.Position = 0; 
                if (receiveResult.MessageType == WebSocketMessageType.Text)
                {
                    
                    var message = Encoding.UTF8.GetString(messageStream.GetBuffer(), 0, (int)messageStream.Length);
                    
                    _messages.OnNext(message); 
                    await _webSocketClientCallback.OnMessage(message); 
                }
                else if (receiveResult.MessageType == WebSocketMessageType.Binary)
                {
                    
                    
                    var binaryData = new ReadOnlyMemory<byte>(messageStream.ToArray()); 
                    _binaryMessages.OnNext(binaryData); 
                                                        
                                                        
                }
            }
        }
        catch (OperationCanceledException oCEx) when (loopCancellationToken.IsCancellationRequested)
        {

            ReportError(oCEx);
        }
        catch (WebSocketException ex) when (ex.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
        {
            
            ReportError(ex);
            await HandleConnectionClosedOrFailedAsync("Connection closed prematurely.");
        }
        catch (Exception ex)
        {
            
            ReportError(ex);
            
            await HandleConnectionClosedOrFailedAsync($"Receive loop error: {ex.Message}");
        }
        finally
        {
            
            if (buffer != null)
                ArrayPool<byte>.Shared.Return(buffer); 
           messageStream?.Dispose(); 
        }

        
        
        
    }


    private void ReportError(Exception exception)
    {
        if (_disposed)
            return; 

        _errors.OnNext(exception);

        
        try
        {
            _webSocketClientCallback.OnError(exception);
        }
        catch (Exception cbEx)
        {
            Console.WriteLine(cbEx.Message);
        }

        
        lock (_stateLock)
        {
            if (State != WebSocketState.Closed)
            {
                UpdateState(WebSocketState.Error);
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
            return;

        if (disposing)
        {
            
            lock (_stateLock) 
            {
                UpdateState(WebSocketState.Closed); 
                _internalCts?.Cancel(); 
                _internalCts?.Dispose();
                _internalCts = null;
            }

            
            _webSocket?.Dispose();
            _webSocket = null;

            
            _messages.OnCompleted();
            _messages.Dispose();
            _binaryMessages.OnCompleted();
            _binaryMessages.Dispose();
            _errors.OnCompleted();
            _errors.Dispose();
            _stateChanges.OnCompleted();
            _stateChanges.Dispose();
        }
        _disposed = true;
    
    }
    
    private async Task CloseInternalAsync(WebSocketCloseStatus closeStatus, string statusDescription,
        bool userInitiated, CancellationToken cancellationToken)
    {
        if (_disposed)
        {
            return;
        }

        ClientWebSocket socketToClose = null;
        CancellationTokenSource internalCtsToCancel = null;

        lock (_stateLock)
        {
            
            if (State is WebSocketState.Closed or WebSocketState.None)
            {
                
                return;
            }

            internalCtsToCancel = _internalCts; 
            _internalCts = null; 

            socketToClose = _webSocket;
            _webSocket = null; 

            UpdateState(WebSocketState.Closed);
            Debug.WriteLine($"was done by user? {userInitiated}");
        }

        
        if (internalCtsToCancel != null)
        {

#if NET8_0_OR_GREATER
            await internalCtsToCancel.CancelAsync();
#else
            internalCtsToCancel.Cancel();
#endif
            internalCtsToCancel.Dispose();
        }


        if (socketToClose != null)
        {
            
            try
            {
                
                using var closeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                closeCts.CancelAfter(TimeSpan.FromSeconds(5)); 

                if (socketToClose.State == System.Net.WebSockets.WebSocketState.Open ||
                    socketToClose.State == System.Net.WebSockets.WebSocketState.CloseReceived) 
                {
                    await socketToClose.CloseAsync(closeStatus, statusDescription, closeCts.Token);
                    
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException) 
            {

                ReportError(ex);

            }
            finally
            {
                
                socketToClose.Dispose();
            }
        }

        
        try
        {
            _webSocketClientCallback.OnClose(closeStatus, statusDescription);
        }
        catch (Exception ex)
        {
            
            ReportError(ex); 
        }
    }


    private async Task HandleConnectionClosedOrFailedAsync(string reason, WebSocketCloseStatus? status = null, string? description = null)
    {



        await CloseInternalAsync(WebSocketCloseStatus.NormalClosure, reason, false, CancellationToken.None);
        
        if (State != WebSocketState.Closed)
        {
            UpdateState(WebSocketState.Error);
        }
    }
   
    private async Task ConnectWithRetryAsync(CancellationToken combinedToken)
    {
        

        while (!combinedToken.IsCancellationRequested && _reconnectionAttempts < MaxReconnectionAttempts)
        {
            ClientWebSocket newWebSocket = null;
            try
            {
                newWebSocket = new ClientWebSocket();
                newWebSocket.Options.KeepAliveInterval = _keepAliveInterval; 

                
                await newWebSocket.ConnectAsync(_hostUri, combinedToken);

                
                
                _webSocket = newWebSocket; 
                UpdateState(WebSocketState.Open);
                _reconnectionAttempts = 0; 

                
                await _webSocketClientCallback.OnOpen();
                _ = Task.Run(() => ReceiveLoopAsync(_internalCts.Token), combinedToken); 

                return; 
            }
            catch (OperationCanceledException) when (combinedToken.IsCancellationRequested)
            {
                
                newWebSocket?.Dispose(); 
                throw ; 
            }
            catch (Exception ex)
            {
                newWebSocket?.Dispose(); 
                
                ReportError(ex); 

                _reconnectionAttempts++;
                if (_reconnectionAttempts >= MaxReconnectionAttempts || combinedToken.IsCancellationRequested)
                {
                    break; 
                }

                int delayMs = CalculateReconnectDelay();
                
                try
                {
                    await Task.Delay(delayMs, combinedToken);
                }
                catch (OperationCanceledException oCEx)
                {
                    throw new OperationCanceledException(oCEx.Message); 
                }
            }
        }

        
        var finalException = new Exception($"Failed to connect after {MaxReconnectionAttempts} attempts.");
        
        ReportError(finalException);
        await HandleConnectionClosedOrFailedAsync("Max reconnection attempts reached.");
        
        
    }

    private void UpdateState(WebSocketState newState)
    {
        
        if (_disposed)
            return;

        lock (_stateLock) 
        {
            if (_currentState != newState)
            {
                
                _currentState = newState;
                _stateChanges.OnNext(newState); 
                try
                {
                    _webSocketClientCallback.OnStateChanged(); 
                }
                catch (Exception ex)
                {
                    
                    
                    ReportError(ex);
                }
            }
        }
    }
    private int CalculateReconnectDelay()
    {
        
        return (int)Math.Min(InitialReconnectDelayMs * Math.Pow(2, _reconnectionAttempts - 1), MaxReconnectDelayMs);
    }

    
    public Task CloseAsync(WebSocketCloseStatus closeStatus = WebSocketCloseStatus.NormalClosure, string statusDescription = "Client requested close", CancellationToken cancellationToken = default)
    {   
        return CloseInternalAsync(closeStatus, statusDescription, true, cancellationToken);
    }

}