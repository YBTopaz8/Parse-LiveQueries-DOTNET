
This is a major release that includes a significant refactor of the underlying WebSocket engine, modernization of the public API to even more async, and the introduction of several key features to improve resilience and developer experience.

## 💥 BREAKING CHANGES

This version introduces several breaking changes to the public API to align with modern .NET Task-based Asynchronous Patterns (TAP) and improve overall robustness.

*   **Async Public API:** All major lifecycle and communication methods are now fully asynchronous and support `CancellationToken`.
    *   `IWebSocketClient.Open()` is now `Task OpenAsync(CancellationToken cancellationToken = default)`.
    *   `IWebSocketClient.Close()` is now `Task CloseAsync(WebSocketCloseStatus closeStatus, string statusDescription, CancellationToken cancellationToken = default)`.
    *   `IWebSocketClient.Send(string message)` is now `Task SendAsync(string message, CancellationToken cancellationToken = default)`.
*   **Client Lifecycle Management:** The client's connection lifecycle is now managed explicitly.
    *   The implicit `ConnectIfNeeded()` and `Reconnect()` methods have been replaced by an explicit `Start()` method to initiate the connection and enable auto-reconnection.
    *   The `Disconnect()` method has been replaced by `Task StopAsync()` to cleanly shut down the client, disconnect, and disable auto-reconnection.
*   **`WebSocketClientFactory` Signature Change:** The delegate signature has been simplified. The `bufferSize` parameter has been removed as it is now managed internally by the `WebSocketClient`.
    *   Old: `delegate IWebSocketClient WebSocketClientFactory(Uri hostUri, IWebSocketClientCallback cb, int bufferSize)`
    *   New: `delegate IWebSocketClient WebSocketClientFactory(Uri hostUri, IWebSocketClientCallback cb)`
*   **Event Handling Rerouted:** The global `ParseLiveQueryClient.OnObjectEvent` observable has been removed. Object events are now routed *directly* to the specific `Subscription<T>` instance that they belong to. This provides a more intuitive and type-safe way to handle events.
*   **`IWebSocketClientCallback.OnClose()` Signature Change:** The `OnClose()` method now provides detailed information about why the connection was closed.
    *   Old: `void OnClose()`
    *   New: `void OnClose(WebSocketCloseStatus? status, string description)`
*   **`OnDisconnected` Observable Change:** The `OnDisconnected` observable now emits a `DisconnectInfo` record, providing richer contextual information (e.g., `CloseStatus`, `Reason`) instead of a simple tuple.

## 🚀 New Features

*   **Traditional C# Event Handlers:** In addition to the existing `System.Reactive` (Rx) observables, `Subscription<T>` now exposes traditional C# events (`OnCreate`, `OnUpdate`, `OnDelete`, `OnEnter`, `OnLeave`) for a more familiar event-driven programming model. Extension methods (`.On(...)`, `.OnUpdate(...)`) have been added for easy attachment.
*   **Async Disposal:** `ParseLiveQueryClient` now implements `IAsyncDisposable`. You can now properly await its disposal using `await using var client = new ParseLiveQueryClient();`.
*   **Formal Connection State Machine:** The client now exposes a `LiveQueryConnectionState` enum (`Disconnected`, `Connecting`, `Connected`, `Reconnecting`, `Failed`) and an `OnConnectionStateChanged` observable, providing transparent insight into the client's current status.
*   **Binary Message Support (Preview):** The `IWebSocketClient` interface now includes an `IObservable<ReadOnlyMemory<byte>> BinaryMessages` stream, laying the groundwork for handling binary data from the server.
*   **Detailed Disconnect Information:** The `OnDisconnected` observable now provides a `DisconnectInfo` record, which includes the close status and reason, and a flag indicating if the disconnect was user-initiated.

## ✨ Improvements & Refinements

*   **WebSocket Engine Overhaul:** The `WebSocketClient` has been completely rewritten for superior stability and performance.
    *   **Connection Resilience:** The connection logic is now managed in a dedicated `ConnectWithRetryAsync` loop with exponential backoff, making it far more resilient to transient network failures.
    *   **Centralized State Management:** A new `UpdateState` method ensures that all state transitions are managed cleanly and broadcast to observers.
    *   **Graceful Shutdown:** A new `CloseInternalAsync` method centralizes shutdown logic, ensuring `CancellationTokenSource`s are cancelled, sockets are closed gracefully, and resources are disposed of correctly, preventing race conditions.
    *   **Performance:** The receive loop now uses `ArrayPool<byte>.Shared` and `MemoryStream` to significantly reduce memory allocations and GC pressure during message processing.
    *   **Native Keep-Alive:** The manual ping/pong mechanism has been replaced by leveraging the native `ClientWebSocket.Options.KeepAliveInterval`, simplifying the implementation and reducing network chatter.
*   **Thread Safety:** The core `ParseLiveQueryClient` has improved thread safety by introducing a `_stateLock` for managing its connection state.
*   **Error Propagation:** The `TaskQueueWrapper` now correctly propagates exceptions from `EnqueueOnSuccess` tasks, preventing them from being silently swallowed.
*   **Code Organization:** The utility `ObjectMapper` has been removed from the `ParseLiveQueryClient.cs` file, improving separation of concerns.

## 🐛 Bug Fixes

*   **Addressed Reconnection Race Conditions:** The previous architecture, where reconnection was attempted from within the `ReceiveLoopAsync`, could lead to race conditions and an unstable state. The redesigned connection logic in `WebSocketClient` completely resolves this.
*   **Corrected Exception Handling in Task Queue:** Ensured that exceptions thrown during the execution of tasks within `EnqueueOnSuccess` are no longer suppressed and are properly propagated as an `InvalidOperationException`.
*   **Improved `Dispose` Pattern:** The `Dispose` logic in `WebSocketClient` is now more robust, ensuring all reactive subjects are completed and all resources, including the `ClientWebSocket` and `CancellationTokenSource`s, are properly cleaned up.