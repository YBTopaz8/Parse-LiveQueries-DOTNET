using System;
using System.Reactive.Linq;
using System.Threading.Tasks;

namespace Parse.LiveQuery;

/// <summary>
/// Interface for a WebSocket client supporting state changes, messages, and errors as observables.
/// </summary>
public interface IWebSocketClient
{
    Task Open();
    Task Close();
    Task Send(string message);
    WebSocketState State { get; }
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