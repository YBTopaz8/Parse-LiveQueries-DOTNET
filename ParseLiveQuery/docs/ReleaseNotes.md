# Release Notes: v3.8.4 — Stability, Performance & Memory Hardening

Version **3.8.4** is a major quality-of-life, stability, and performance release. This release focuses on : eliminating UI thread deadlocks, plugging long-term memory leaks, massively reducing GC pressure during high-frequency live events, and closing connection race conditions.

---

### 🌟 Highlights & New Features

* **Diffing on Updates:** The `OnUpdate` extension method now receives **both** the `original` object state and the `updated` object state, allowing you to easily detect which fields changed.
* **Disposable Event Subscriptions:** Extension methods (`On`, `OnUpdate`) now return `IDisposable` tokens. You can now cleanly attach and detach event handlers inside UI pages, controllers, or view models without causing memory leaks.
* **Zero UI Deadlocks:** Comprehensive support for `.ConfigureAwait(false)` across the entire library ensures complete compatibility with WPF, WinForms, MAUI, Uno Platform, and Unity.

---

### 🚀 Performance & Memory Improvements

* **Single-Pass JSON Parsing (`JsonDocument`):** Replaced double-hop dictionary serialization with a high-performance, single-pass `JsonDocument` reader. This dramatically cuts down memory allocations and eliminates GC pauses under high-frequency event streams.
* **Eliminated Rx Leaks:** Wrapped internal reactive subjects in a managed `CompositeDisposable` and ensured that all internal subjects, observable streams, and abandoned `WebSocketClient` instances are cleanly disposed during teardowns and reconnects.
* **Faster Reconnections:** Removed the artificial 100ms sequential throttle delay in the operation queue. Subscriptions and pending messages now resubscribe and drain immediately upon socket connection.

---

### 🔒 Concurrency & Thread-Safety Fixes

* **Prevented Duplicate Connections:** Fixed a race condition in `ConnectIfNeeded()` by locking the state to `Connecting` prior to yielding thread execution, ensuring simultaneous callers cannot spawn duplicate/ghost WebSocket connections.
* **Zero Data Loss on Drops:** If the network drops while draining the operation queue, failed operations are now preserved and re-enqueued rather than discarded.
* **Thread-Safe Teardown:** Fixed a synchronization race between synchronous `Dispose()` and asynchronous `DisposeAsync()`.
* **Crash Prevention:** Enhanced background queue error handling to prevent unhandled ThreadPool exceptions from bringing down the host process.

---

### 🛠️ Breaking Changes & Migration

The delegate signatures in extension methods have been upgraded to return `IDisposable` tokens to prevent memory leaks.

#### Before:
```csharp
// Previously attached via anonymous lambda (could not be cleanly unsubscribed)
subscription.OnUpdate((original, updated) => 
{
    Console.WriteLine($"Updated: {updated.ObjectId}");
});
```

#### Now:
```csharp
// Now returns an IDisposable token for clean lifecycle management
IDisposable subscriptionToken = subscription.OnUpdate((original, updated) => 
{
    Console.WriteLine($"Original Name: {original?.Name}");
    Console.WriteLine($"New Name: {updated?.Name}");
});

// Detach whenever the page/viewmodel is destroyed:
subscriptionToken.Dispose();
```

---

### 📦 Installation
```shell
dotnet add package YB.ParseLiveQueryDotNet --version 3.8.4
```