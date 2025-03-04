# Parse-Live-Query-Unofficial v3 (with RX and LINQ Support)

# Grab the Nuget Here: [![Nuget](https://img.shields.io/nuget/v/yb.parselivequerydotnet.svg)](https://www.nuget.org/packages/YB.ParseLiveQueryDotNet)

# **v3.2.0: Robust LiveQuery Client with Enhanced Reliability and Stability 🚀**

**Key Enhancements:**

*   **Automatic Reconnection:** Implemented automatic reconnection to the LiveQuery server upon connection loss.
    *   Features exponential backoff with configurable retries.
    *   Reports reconnection attempts and failures via the `OnError` observable.

*   **Heartbeat (Ping/Pong):** Integrated WebSocket Ping/Pong frames for robust connection health monitoring.
    *   Detects and recovers from "dead" connections, enhancing stability.

*   **Configurable Receive Buffer:** Added ability to customize the WebSocket receive buffer size.
    *   Useful for handling large messages.
    *   Utilize: `new WebSocketClient(uri, callback, bufferSize);`

*   **Refactored WebSocket Handling:**
    *   Complete rewrite for improved stability, error handling, and performance.
    *   Ensures proper resource disposal (`Dispose()`) to prevent memory leaks.
    *   Provides clear and consistent WebSocket connection state management.
    *   Includes placeholder for binary message handling.
    *   Allows control over WebSocket close status and description.

*   **Optimized Subscription Management:**
    *   Simplified unsubscription process.
    *   Ensures thread-safe subscription operations.
    *   Removed reflection for enhanced performance.

*   **Full `IDisposable` Implementation:** `ParseLiveQueryClient`, `WebSocketClient`, and `Subscription` classes now fully implement `IDisposable`.
    *   Guarantees proper resource cleanup.

*   **Enhanced Error Handling:** Improved error handling with more informative exceptions and events.

*   **Optimized JSON Handling:** Enhanced JSON serialization/deserialization.

**Breaking Changes:**

*   None.  This release maintains backward compatibility with v3.1.0. Thorough testing is still strongly recommended.

**Upgrade Notes:**

*   Thorough application testing is highly recommended after upgrading due to significant internal improvements.
*   New features (reconnection, buffer size) are optional and do not require code changes to maintain existing functionality.

**Happy Coding! 👋🏾**


# v3.1.0 🏷️ Name Your LiveQuery Subscriptions!

✨ Super handy update! That Completely fixes Parse Relations and Operations & You can now NAME your LiveQuery subscriptions! ✨

- Fixed all Parse Relation - related issued 

Give your subscriptions meaningful names to make managing them in your code WAY easier! 

**What's New:**

- Name Your Subs:  Use `LiveClient.Subscribe(query, "MySubscriptionName")` to give your subscriptions client-side names.
- Find Subs by Name: Quickly get your subscriptions back using `LiveClient.GetSubscriptionByName("MySubscriptionName")`.

## Why Name them? 🤔

- Keep Organized: Easily track different subscriptions, especially in bigger apps.
- Easy Access: Grab the exact subscription you need by name, no more digging through lists!
- Code Clarity Make your LiveQuery code cleaner and easier to understand.

 No Breaking Changes! 👍 
Just a smooth, helpful update. Your existing LiveQuery code will keep working perfectly.

Let me know what you think on GitHub!

🚀 Happy Coding! 🚀

# v3.0.1 - QOL update 🎇
## It's no longer REQUIRED to be logged in order to connect to LQ.  
Any client device can connect to Live Queries independently

- Fixed #8  (saves dates as string for now instead of just not saving)
- Fixed #4  
- Closes #7 too 
# v3.0.0 🎄

##### Major Update! - VERY IMPORTANT FOR SECURITY

Fixed an issues where connected Live Queries would never actually close stream with server (causing memory leaks)
This eliminates any instance where a "disconnected" user would still be able to receive data from the server and vice versa.
Please update to this version as soon as possible to avoid any security issues.
The best I would suggest is to migrate the currently observed classes to a new class and delete the old one.
You can easily do this by creating a new class and copying the data from the old class to the new one (via ParseCloud in your web interface like in Back4App).

More Fixes...
- Fixed issues where `GetCurrentUIser()` from Parse did **NOT** return `userName` too.
- Fixed issues with `Relations/Pointers` being broken.

Thank You!

## v2.0.4
Improvements on base Parse SDK.
- LogOut now works perfectly fine and doesn't crash app!
- SignUpWithAsync() will now return the Signed up user's info to avoid Over querying.
- Renamed some methods.
- Fixed perhaps ALL previous UNITY crashes.

(Will do previous versions later - I might never even do it )
# Parse-Live-Query-Unofficial v2 (with RX and LINQ Support) - soon

**Key Changes from v1:**  
- Now supports .NET 5, 6, 7, 8, and .NET MAUI.  
- Uses `System.Net.WebSockets.Client` for better connectivity.  
- Replaced callbacks (CB) with Reactive Extensions (Rx.NET) for event handling.  
- Full LINQ support integrated.  
- Enhanced stability and MAUI compatibility.

**Thanks :)** to:
- [JonMcPherson](https://github.com/JonMcPherson/parse-live-query-dotnet) for original Parse Live Query dotnet code.  
- [Parse Community](https://github.com/parse-community) for Parse SDK.  
- My [Parse-SDK fork](https://github.com/YBTopaz8/Parse-SDK-dotNET) which this depends on.  

## What is Live Query?
Live Query provides real-time data sync between server and clients over WebSockets. When data changes on the server, subscribed clients are instantly notified.

## Prerequisites
- Ensure Live Query is enabled for your classes in the Parse Dashboard.

Example in Back4App:  
![Enable LQ](https://github.com/user-attachments/assets/b9cba805-f81a-47e2-a999-ce6864ba438a)

## Basic Setup

### 1. Initialization
```csharp
using Parse; // Parse
using Parse.LiveQuery; // ParseLQ
using System.Reactive.Linq; // For Rx
using System.Linq; // For LINQ
using System.Collections.ObjectModel;

// Check internet
if (Connectivity.NetworkAccess != NetworkAccess.Internet)
{
    Console.WriteLine("No Internet, can't init ParseClient.");
    return;
}

// Init ParseClient
var client = new ParseClient(new ServerConnectionData
{
    ApplicationID = "YOUR_APP_ID",
    ServerURI = new Uri("YOUR_SERVER_URL"),
    Key = "YOUR_CLIENT_KEY" // or MasterKey
}, new HostManifestData
{
    Version = "1.0.0",
    Identifier = "com.yourcompany.yourmauiapp",
    Name = "MyMauiApp"
});

client.Publicize(); // Access via ParseClient.Instance globally
```

### 2. Simple Subscription Setup
```csharp
// Create LiveQuery client
ParseLiveQueryClient LiveClient = new ParseLiveQueryClient();

void SetupLiveQuery()
{
    try
    {
        var query = ParseClient.Instance.GetQuery("TestChat");
        var subscription = LiveClient.Subscribe(query);
        
        LiveClient.ConnectIfNeeded();

        // Rx event streams
        LiveClient.OnConnected
            .Subscribe(_ => Debug.WriteLine("LiveQuery connected."));
        LiveClient.OnDisconnected
            .Subscribe(info => Debug.WriteLine(info.userInitiated 
                ? "User disconnected." 
                : "Server disconnected."));
        LiveClient.OnError
            .Subscribe(ex => Debug.WriteLine("LQ Error: " + ex.Message));
        LiveClient.OnSubscribed
            .Subscribe(e => Debug.WriteLine("Subscribed to: " + e.requestId));

        // Handle object events (Create/Update/Delete)
        LiveClient.OnObjectEvent
            .Where(e => e.subscription == subscription)
            .Subscribe(e =>
            {
                Debug.WriteLine($"Event {e.evt} on object {e.objState.ObjectId}");
            });

    }
    catch (Exception ex)
    {
        Debug.WriteLine("SetupLiveQuery Error: " + ex.Message);
    }
}
```

# Examples

Below are 15+ examples demonstrating LINQ queries, Rx usage, and their combination to handle Live Query events. We will show various scenarios on a hypothetical "TestChat" class with fields: `Message`, `Author`, `CreatedAt`.

**Legend:**  
- **Simple:** Basic query & subscription.  
- **Medium:** Introduce some filtering & conditions.  
- **Robust:** Complex queries, multiple conditions, ordering, and advanced Rx usage.  
- **Very Robust (LINQ+RX):** Combining reactive operators & LINQ to handle intricate, real-time data flows.

## 7 LINQ Examples (2 Simple, 2 Medium, 3 Robust)

### LINQ Example 1 (Simple): Subscribe to all "TestChat" messages
```csharp
var q = ParseClient.Instance.GetQuery("TestChat"); 
var sub = LiveClient.Subscribe(q);
LiveClient.OnObjectEvent
    .Where(e => e.subscription == sub)
    .Subscribe(e => Debug.WriteLine("New Event: " + e.evt));
LiveClient.ConnectIfNeeded();
```

### LINQ Example 2 (Simple): Filter by Author = "John"
```csharp
var q = ParseClient.Instance.GetQuery("TestChat")
    .WhereEqualTo("Author", "John");
var sub = LiveClient.Subscribe(q);
LiveClient.OnObjectEvent
    .Where(e => e.subscription == sub)
    .Subscribe(e => Debug.WriteLine("John-related event: " + e.objState.ObjectId));
```

### LINQ Example 3 (Medium): Messages created after a specific date
```csharp
DateTime limitDate = DateTime.UtcNow.AddDays(-1);
var q = ParseClient.Instance.GetQuery("TestChat")
    .WhereGreaterThan("CreatedAt", limitDate);
var sub = LiveClient.Subscribe(q);
LiveClient.OnObjectEvent
    .Where(e => e.subscription == sub)
    .Subscribe(e => Debug.WriteLine("Recent message event: " + e.objState.ObjectId));
```

### LINQ Example 4 (Medium): Filter by Author starting with "A" and has a non-empty "Message"
```csharp
var q = ParseClient.Instance.GetQuery("TestChat")
    .WhereStartsWith("Author", "A")
    .WhereExists("Message");
var sub = LiveClient.Subscribe(q);
LiveClient.OnObjectEvent
    .Where(e => e.subscription == sub)
    .Subscribe(e => Debug.WriteLine("A's event: " + e.objState.ObjectId));
```

### LINQ Example 5 (Robust): Complex filtering by multiple conditions and ordering
```csharp
var q = ParseClient.Instance.GetQuery("TestChat")
    .WhereNotEqualTo("Author", "SpamBot")
    .WhereGreaterThanOrEqualTo("CreatedAt", DateTime.UtcNow.AddHours(-2))
    .OrderByDescending("CreatedAt")
    .Limit(50);
var sub = LiveClient.Subscribe(q);
LiveClient.OnObjectEvent
    .Where(e => e.subscription == sub)
    .Subscribe(e => 
    {
        // Possibly transform object to your model
        Debug.WriteLine("Robust event: " + e.objState.ObjectId);
    });
```

### LINQ Example 6 (Robust): Multiple fields check and ignoring certain authors
```csharp
var q = ParseClient.Instance.GetQuery("TestChat")
    .WhereNotContainedIn("Author", new[] { "BannedUser1", "BannedUser2" })
    .WhereMatches("Message", "urgent", "i"); // Case-insensitive regex
var sub = LiveClient.Subscribe(q);
LiveClient.OnObjectEvent
    .Where(e => e.subscription == sub)
    .Subscribe(e => Debug.WriteLine("Urgent event: " + e.objState.ObjectId));
```

### LINQ Example 7 (Robust): Chain multiple queries using union (not built-in, but simulate using multiple subs)
```csharp
// Query 1: Messages from Author = "TeamLead"
var q1 = ParseClient.Instance.GetQuery("TestChat").WhereEqualTo("Author", "TeamLead");
// Query 2: Messages containing "ProjectX"
var q2 = ParseClient.Instance.GetQuery("TestChat").WhereMatches("Message", "ProjectX");

var sub1 = LiveClient.Subscribe(q1);
var sub2 = LiveClient.Subscribe(q2);

LiveClient.OnObjectEvent
    .Where(e => e.subscription == sub1 || e.subscription == sub2)
    .Subscribe(e => Debug.WriteLine("TeamLead/ProjectX Event: " + e.objState.ObjectId));
```

## 6 Rx Examples (2 Simple, 2 Medium, 2 Robust)

### RX Example 1 (Simple): OnConnected, OnDisconnected logging
```csharp
LiveClient.OnConnected.Subscribe(_ => Debug.WriteLine("Connected via Rx."));
LiveClient.OnDisconnected.Subscribe(info => Debug.WriteLine("Disconnected via Rx."));
LiveClient.ConnectIfNeeded();
```

### RX Example 2 (Simple): OnError retry logic
```csharp
LiveClient.OnError
    .Retry(3) // Try reconnect or re-subscribe up to 3 times
    .Subscribe(ex => Debug.WriteLine("Error after retries: " + ex.Message));
```

### RX Example 3 (Medium): Buffer incoming Create events for batch processing
```csharp
var subscription = LiveClient.Subscribe(ParseClient.Instance.GetQuery("TestChat"));
LiveClient.OnObjectEvent
    .Where(e => e.subscription == subscription && e.evt == Subscription.Event.Create)
    .Buffer(TimeSpan.FromSeconds(5)) // Collect events for 5s
    .Subscribe(batch =>
    {
        Debug.WriteLine($"Received {batch.Count} new messages in last 5s.");
    });
LiveClient.ConnectIfNeeded();
```

### RX Example 4 (Medium): Throttle frequent updates
```csharp
var sub = LiveClient.Subscribe(ParseClient.Instance.GetQuery("TestChat"));
LiveClient.OnObjectEvent
    .Where(e => e.subscription == sub && e.evt == Subscription.Event.Update)
    .Throttle(TimeSpan.FromSeconds(2)) // If multiple updates occur quickly, take the last after 2s
    .Subscribe(e => Debug.WriteLine("Throttled update event: " + e.objState.ObjectId));
```

### RX Example 5 (Robust): Combine OnError and OnObjectEvent streams
```csharp
var sub = LiveClient.Subscribe(ParseClient.Instance.GetQuery("TestChat"));
var events = LiveClient.OnObjectEvent.Where(e => e.subscription == sub);
var errors = LiveClient.OnError;

events.Merge(errors.Select(ex => new ObjectEventArgs { evt = Subscription.Event.Enter, objState = null }))
    .Subscribe(e =>
    {
        if (e.objState == null)
            Debug.WriteLine("Merged: Error event occurred.");
        else
            Debug.WriteLine("Merged: Normal object event " + e.objState.ObjectId);
    });
LiveClient.ConnectIfNeeded();
```

### RX Example 6 (Robust): Use Replay to keep last known event and switch queries dynamically
```csharp
var replayed = LiveClient.OnObjectEvent.Replay(1);
replayed.Connect(); // Start replaying
var subA = LiveClient.Subscribe(ParseClient.Instance.GetQuery("TestChat").WhereEqualTo("Author", "AUser"));
var subB = LiveClient.Subscribe(ParseClient.Instance.GetQuery("TestChat").WhereEqualTo("Author", "BUser"));

var combined = LiveClient.OnObjectEvent.Where(e => e.subscription == subA || e.subscription == subB);
combined.Subscribe(e => Debug.WriteLine("Replayed combined event: " + e.objState.ObjectId));
LiveClient.ConnectIfNeeded();
```

## 3 Very Robust Examples (Rx + LINQ Combined)

### Rx+LINQ Example 1 (Very Robust): Filter events in-flight with LINQ, buffer and process after a condition
Scenario: We only want messages with "Critical" keyword and Author != "SystemBot", batch them every 10s.
```csharp
var q = ParseClient.Instance.GetQuery("TestChat");
var s = LiveClient.Subscribe(q);

LiveClient.OnObjectEvent
    .Where(e => e.subscription == s && e.evt == Subscription.Event.Create)
    .Select(e => e.objState)
    .Where(o => o.ContainsKey("Message") && ((string)o["Message"]).Contains("Critical"))
    .Where(o => (string)o["Author"] != "SystemBot")
    .Buffer(TimeSpan.FromSeconds(10))
    .Where(batch => batch.Count > 0)
    .Subscribe(batch =>
    {
        // LINQ over batch
        var sorted = batch.OrderByDescending(o => (DateTime)o["CreatedAt"]);
        foreach (var msg in sorted)
            Debug.WriteLine("Critical Msg: " + msg["Message"]);
    });

LiveClient.ConnectIfNeeded();
```

### Rx+LINQ Example 2 (Very Robust): Throttle + GroupBy 
Scenario: Group updates by Author every 5s and print how many updates each author did.
```csharp
var q = ParseClient.Instance.GetQuery("TestChat");
var s = LiveClient.Subscribe(q);

LiveClient.OnObjectEvent
    .Where(e => e.subscription == s && e.evt == Subscription.Event.Update)
    .Select(e => e.objState)
    .GroupBy(o => (string)o["Author"])
    .SelectMany(group =>
        group.Buffer(TimeSpan.FromSeconds(5))
             .Select(list => new { Author = group.Key, Count = list.Count })
    )
    .Subscribe(authorGroup =>
    {
        Debug.WriteLine($"{authorGroup.Author} made {authorGroup.Count} updates in the last 5s");
    });

LiveClient.ConnectIfNeeded();
```

### Rx+LINQ Example 3 (Very Robust): Merge two queries, distinct by ObjectId, and order results on the fly
Scenario: We want to combine messages from Authors "X" and "Y", remove duplicates, sort by CreatedAt descending, and only act on the top 10 recent unique messages each 10s.
```csharp
var qX = ParseClient.Instance.GetQuery("TestChat").WhereEqualTo("Author", "X");
var qY = ParseClient.Instance.GetQuery("TestChat").WhereEqualTo("Author", "Y");

var sX = LiveClient.Subscribe(qX);
var sY = LiveClient.Subscribe(qY);

LiveClient.OnObjectEvent
    .Where(e => (e.subscription == sX || e.subscription == sY) && 
                 (e.evt == Subscription.Event.Create || e.evt == Subscription.Event.Update))
    .Select(e => e.objState)
    .Buffer(TimeSpan.FromSeconds(10))
    .Select(batch => batch
        .GroupBy(o => o.ObjectId)
        .Select(g => g.First()) // distinct by ObjectId
        .OrderByDescending(o => (DateTime)o["CreatedAt"])
        .Take(10))
    .Subscribe(top10 =>
    {
        foreach (var msg in top10)
            Debug.WriteLine("Top message from X/Y: " + msg["Message"]);
    });

LiveClient.ConnectIfNeeded();
```

---

## Implementing a Connection Listener (Optional)
```csharp
public class MyLQListener : IParseLiveQueryClientCallbacks
{
    public void OnLiveQueryClientConnected(ParseLiveQueryClient client)
    {
        Debug.WriteLine("Client Connected");
    }

    public void OnLiveQueryClientDisconnected(ParseLiveQueryClient client, bool userInitiated)
    {
        Debug.WriteLine("Client Disconnected");
    }

    public void OnLiveQueryError(ParseLiveQueryClient client, LiveQueryException reason)
    {
        Debug.WriteLine("LiveQuery Error: " + reason.Message);
    }

    public void OnSocketError(ParseLiveQueryClient client, Exception reason)
    {
        Debug.WriteLine("Socket Error: " + reason.Message);
    }
}

```

## Conclusion
This v3 version integrates LINQ and Rx.NET, enabling highly flexible and reactive real-time data flows with Parse Live Queries. Advanced filtering, buffering, throttling, and complex transformations are now possible with minimal code.

PRs are welcome!

