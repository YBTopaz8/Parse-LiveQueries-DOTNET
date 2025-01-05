### Parse-Live-Query-Unofficial
- Now on V2.0.1 ! 🚀
- Added Support for .NET 5,6,7,8, 9 and .NET MAUI.
- Replaced previous web client with `System.Net.WebSockets.Client` as I believe is better.
- Replaced Subscriptions and callbacks with `System.Reactive` for better handling of events.
- Added a YouTube video for a full walkthrough of the SDK.
- Added a full ReadMe for the SDK.



Here is the full ReadMe;

Since I Updated this based on my MAUI Projects, I had to update my fork of Parse SDK to have MAUI support, thus 
## THIS is DEPENDENT on my [ParseSDK fork](https://github.com/YBTopaz8/Parse-SDK-dotNET). 

Will release a Nuget version later for both.

### What are Live Queries?
Glad you asked!
So to put it VERY SIMPLE : Live Queries lets you build a **RealTime Chat App** in less than idk 25 lines of code? (Excluding UI ofc).

In "Long" it essentially opens a **Websocket/caller-listener tunnel** where _WHATEVER_ changes done to the subscribed class/table(for sql folks), will reflect to ALL listening clients in "real time" (very very minimal delay. Like and Eye blink's delay!)

Example: You Create a Class/Table (for SQL folks) then tell the server "Whatever is done here _TELL EVERYONE WHO NEEDS TO BE INFORMED ASAP_ ", then the client apps (your developed app) will subscribe to the _EVENTUAL POSSIBILITIES_ of any change happening to your class/table records.

As such. 
1. Device A Subscribes to class/table "Comments" found in Server.
2. Device B subscribes to "Comments" too.
3. Device A -> Server (A sends to server)
4. Device A <- Server -> Device B (Server sends back data to both devices _ASSUMING THEY ARE ALLOWED TO VIEW THE CHANGES_, you can configure this with ACL!)

I hope it's a OVERexplanation now haha! But if you any more specific questions, please shoot!

How To Use In addition to the docs over from the folks at ;
For now:

### STEP 0 (VERY IMPORTANT): MAKE SURE YOUR ENABLE LIVE QUERY ON THE CLASSES NEEDED.
I used Back4Apps for dev/testing so the 2 classes I needed were checked!
(Please check the Git Repo)


### 1. Download the project's source and add to your solution , your solution will look like this
(Please check the Git Repo)
### 2. You will have to initialize your ParseClient with your app's details
```
// Check for internet connection
if (Connectivity.NetworkAccess != NetworkAccess.Internet)
{
    Console.WriteLine("No Internet Connection: Unable to initialize ParseClient.");
    return false;
}

// Create ParseClient
ParseClient client = new ParseClient(new ServerConnectionData
{
    ApplicationID = APIKeys.ApplicationId, 
    ServerURI = APIKeys.ServerUri,
    Key = APIKeys.DotNetKEY, // Or use MASTERKEY

}
);
HostManifestData manifest = new HostManifestData() // Optional but I really recommend setting just to avoid potential crashes
{
    Version = "1.0.0",
    Identifier = "com.yvanbrunel.myMauiApp",
    Name = "myAppName",
};

client.Publicize(); // Best to use if you want to access ParseClient.Instance anywhere in the app.
```
### 3. Setup Live Queries

A subscription can be very easily handled as such
I copied the code from my Sample Demo App (Here)[https://github.com/YBTopaz8/Chat-App-.NET-MAUI-LIVE-QUERY];
```

    //I Will Just leave all this code in Docs because believe it or not, sometimes even I forget how to use my own lib :D
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
                    Debug.WriteLine($"Message before {Message?.Length}");
                    TestChat chat = new();
                    var objData = (e.objectDictionnary as Dictionary<string, object>);

                    switch (e.evt)
                    {
                        
                        case Subscription.Event.Enter:
                            Debug.WriteLine("entered");
                            break;
                        case Subscription.Event.Leave:
                            Debug.WriteLine("Left");
                            break;
                            case Subscription.Event.Create:
                            chat = ObjectMapper.MapFromDictionary<TestChat>(objData);
                            Messages.Add(chat);
                            break;
                            case Subscription.Event.Update:
                            chat = ObjectMapper.MapFromDictionary<TestChat>(objData);
                            var obj = Messages.FirstOrDefault(x => x.UniqueKey == chat.UniqueKey);
                            
                            Messages.RemoveAt(Messages.IndexOf(obj));
                            Messages.Add(chat);

                            break;
                            case Subscription.Event.Delete:
                            chat = ObjectMapper.MapFromDictionary<TestChat>(objData);
                            var objj = Messages.FirstOrDefault(x => x.UniqueKey == chat.UniqueKey);

                            Messages.RemoveAt(Messages.IndexOf(objj));
                            break;
                        default:
                            break;
                    }
                    Debug.WriteLine($"Message after {Message.Length}");
                    Debug.WriteLine($"Event {e.evt} on object {e.objectDictionnary.GetType()}");
                });

        }
        catch (Exception ex)
        {
            Debug.WriteLine("SetupLiveQuery Error: " + ex.Message);
        }
    }

```
If you need a connection listener, you can set a class as 
```
public class CLASS_NAME_FOR_LISTENERCLASS: ObservableObject, IParseLiveQueryClientCallbacks
{
//You will NEED to implement 
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
     Debug.WriteLine("Error " + reason.Message);
 }

 public void OnSocketError(ParseLiveQueryClient client, Exception reason)
 {
     Debug.WriteLine("Socket Error ");
 }
}
```
That's it!
I'll be updating the SDK with any new features if any, The .NET SDK had no Equivalent but we do now ! 
This is a simple "Port" with ~~no real fixes implemented~~ Fixes from the Parse SDK itself, and some PRs from the forked repo (and except for unwanted crashes) and no security updates (if any, after Nov 27th 2024, when I prepared this release)~~either~~.
PRs are welcomed!
- Many thanks to [JohnMcPherson](https://github.com/JonMcPherson/parse-live-query-dotnet) for [Parse-Live-Query-Dotnet](https://github.com/JonMcPherson/parse-live-query-dotnet) . I would absolutely NOT have done it  without you!
- [Parse Community](https://github.com/parse-community)

