# Parse-Live-Query-Unofficial v3 (with RX and LINQ Support)

# Grab the Nuget Here: [![Nuget](https://img.shields.io/nuget/v/yb.parselivequerydotnet.svg)](https://www.nuget.org/packages/YB.ParseLiveQueryDotNet)

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
