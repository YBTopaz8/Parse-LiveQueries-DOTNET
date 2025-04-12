using System;
using System.Net.WebSockets;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Parse.LiveQuery;

/// <summary>
/// Interface for a WebSocket client supporting state changes, messages, and errors as observables.
/// </summary>
public interface IWebSocketClient
{
    Task OpenAsync(CancellationToken cancellationToken = default); // Added CancellationToken
    Task CloseAsync(WebSocketCloseStatus closeStatus = WebSocketCloseStatus.NormalClosure, string statusDescription = "Client requested close", CancellationToken cancellationToken = default); // Added CancellationToken and defaults
    Task SendAsync(string message, CancellationToken cancellationToken = default); // Added CancellationToken

    WebSocketState State { get; }

    /// <summary>
    /// Observable sequence of WebSocket state changes.
    /// </summary>
    IObservable<WebSocketState> StateChanges { get; }

    /// <summary>
    /// Observable sequence of received text messages.
    /// </summary>
    IObservable<string> Messages { get; }

    /// <summary>
    /// Observable sequence of received binary messages.
    /// </summary>
    IObservable<ReadOnlyMemory<byte>> BinaryMessages { get; } // Added

    /// <summary>
    /// Observable sequence of exceptions encountered by the client.
    /// </summary>
    IObservable<Exception> Errors { get; }
}


/// <summary>
/// Represents the state of a WebSocket connection.
/// </summary>
public enum WebSocketState
{
    Open,
    Closed,
    Connecting,
    Error,
    None
}