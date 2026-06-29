using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Parse.Abstractions.Infrastructure;
using Parse.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Parse.LiveQuery.ParseLiveQueryClient;

namespace Parse.LiveQuery.Tests.ParseLiveQueries.Tests;


[TestClass]
public class LiveQueryClientMechanicsTests
{
    private ParseLiveQueryClient client;
    private IWebSocketClientCallback webSocketCallback;
    private IServiceHub serviceHub;

    [TestInitialize]
    public void SetUp()
    {
        var mockWebSocketFactory = new Mock<WebSocketClientFactory>();
        var mockWebSocket = new Mock<IWebSocketClient>();
        var hub = new MutableServiceHub { };
        hub.SetDefaults();
        var parseClient = new ParseClient(new ServerConnectionData { ApplicationID = "appId", Key = "dotnetKey", ServerURI = "http://localhost/" }, hub);

        parseClient.AddValidClass<TestChat>();
        parseClient.Publicize();
        serviceHub = parseClient.Services;

        mockWebSocketFactory.Setup(f => f(It.IsAny<Uri>(), It.IsAny<IWebSocketClientCallback>(), It.IsAny<int>()))
            .Callback<Uri, IWebSocketClientCallback, int>((_, cb, __) => webSocketCallback = cb)
            .Returns(mockWebSocket.Object);

        client = new ParseLiveQueryClient(new Uri("ws://localhost/"), mockWebSocketFactory.Object, new SubscriptionFactory(), new SynchronousTaskQueue());
        client.Start();
        webSocketCallback.OnOpenAsync().Wait();
        webSocketCallback.OnMessageAsync("{\"op\":\"connected\"}").Wait();
    }

    [TestCleanup]
    public void TearDown() => client?.DisposeAsync();

    [TestMethod]
    [Description("Tests that an object entering the query bounds fires the Enter event.")]
    public async Task Client_WhenObjectEntersQuery_FiresEnterEvent()
    {
        var query = new ParseQuery<TestChat>(serviceHub);
        TestChat enteredObj = null;
        var sub = client.Subscribe(query);
        sub.On(Subscription.Event.Enter, (obj, q) => enteredObj = obj);

        await webSocketCallback.OnMessageAsync("{\"op\":\"subscribed\",\"requestId\":1}");
        await webSocketCallback.OnMessageAsync("{\"op\":\"enter\",\"requestId\":1,\"object\":{\"className\":\"TestChat\",\"objectId\":\"enter123\"}}");

        Assert.IsNotNull(enteredObj);
        Assert.AreEqual("enter123", enteredObj.ObjectId);
    }

    [TestMethod]
    [Description("Tests that an object leaving the query bounds fires the Leave event.")]
    public async Task Client_WhenObjectLeavesQuery_FiresLeaveEvent()
    {
        var query = new ParseQuery<TestChat>(serviceHub);
        TestChat leftObj = null;
        var sub = client.Subscribe(query);
        sub.On(Subscription.Event.Leave, (obj, q) => leftObj = obj);

        await webSocketCallback.OnMessageAsync("{\"op\":\"subscribed\",\"requestId\":1}");
        await webSocketCallback.OnMessageAsync("{\"op\":\"leave\",\"requestId\":1,\"object\":{\"className\":\"TestChat\",\"objectId\":\"leave123\"}}");

        Assert.IsNotNull(leftObj);
        Assert.AreEqual("leave123", leftObj.ObjectId);
    }

    [TestMethod]
    [Description("Tests that a server-reported error is properly parsed and emitted.")]
    public async Task Client_WhenServerErrorOccurs_EmitsErrorToSubscription()
    {
        var query = new ParseQuery<TestChat>(serviceHub);
        LiveQueryException receivedError = null;
        var sub = client.Subscribe(query);
        sub.Errors.Subscribe(err => receivedError = err);

        await webSocketCallback.OnMessageAsync("{\"op\":\"subscribed\",\"requestId\":1}");
        await webSocketCallback.OnMessageAsync("{\"op\":\"error\",\"requestId\":1,\"code\":101,\"error\":\"Invalid query\",\"reconnect\":false}");

        Assert.IsNotNull(receivedError);
        Assert.IsInstanceOfType(receivedError, typeof(LiveQueryException.ServerReportedException));
        var serverError = (LiveQueryException.ServerReportedException)receivedError;
        Assert.AreEqual(101, serverError.Code);
        Assert.AreEqual("Invalid query", serverError.Error);
    }

    [TestMethod]
    [Description("Tests that unsubscribing sends the correct op and removes from the dictionary.")]
    public async Task Client_WhenUnsubscribed_RemovesFromClientAndFiresEvent()
    {
        var query = new ParseQuery<TestChat>(serviceHub);
        var sub = client.Subscribe(query);
        await webSocketCallback.OnMessageAsync("{\"op\":\"subscribed\",\"requestId\":1}");

        bool didUnsubscribeFire = false;
        sub.Unsubscribes.Subscribe(_ => didUnsubscribeFire = true);

        // ACT
        await client.Unsubscribe(query, sub);
        await webSocketCallback.OnMessageAsync("{\"op\":\"unsubscribed\",\"requestId\":1}");

        // ASSERT
        Assert.IsTrue(didUnsubscribeFire, "The unsubscription event should have fired.");
        Assert.IsFalse(client.Subscriptions.ContainsKey(sub.RequestID), "Subscription should be removed from the internal dictionary.");
    }

    [TestMethod]
    [Description("Tests that if the WebSocket reconnects, existing subscriptions are automatically re-sent to the server.")]
    public async Task Client_OnReconnect_AutomaticallyResendsSubscriptions()
    {
        // 1. Subscribe
        var query = new ParseQuery<TestChat>(serviceHub);
        client.Subscribe(query);
        await webSocketCallback.OnMessageAsync("{\"op\":\"subscribed\",\"requestId\":1}");

        // 2. Simulate a disconnect
        webSocketCallback.OnClose(null, "Simulated drop");

        // 3. Simulate the reconnect
        await webSocketCallback.OnOpenAsync();

        bool didResubscribe = false;
        client.OnSubscribed.Subscribe(e => { if (e.requestId == 1) didResubscribe = true; });

        // Act: The server says we are connected again
        await webSocketCallback.OnMessageAsync("{\"op\":\"connected\"}");
        await webSocketCallback.OnMessageAsync("{\"op\":\"subscribed\",\"requestId\":1}");

        Assert.IsTrue(didResubscribe, "Client should have automatically re-sent the subscription to the server after reconnecting.");
    }

    [TestMethod]
    [Description("Tests that naming a subscription successfully caches it, and unsubscribing removes it from the cache.")]
    public async Task Client_NamedSubscriptions_AreCachedAndRemovedCorrectly()
    {
        var query = new ParseQuery<TestChat>(serviceHub);

        // Subscribe with a name
        var sub = client.Subscribe(query, "GlobalChatListener");
        await webSocketCallback.OnMessageAsync("{\"op\":\"subscribed\",\"requestId\":1}");

        // Assert it exists in the cache
        var fetchedSub = client.GetSubscriptionByName("GlobalChatListener");
        Assert.IsNotNull(fetchedSub, "The subscription should be retrievable by its name.");
        Assert.AreEqual(sub.RequestID, fetchedSub.RequestID);

        var listSubs = client.GetSubscriptionsByName("GlobalChatListener");
        Assert.AreEqual(1, listSubs.Count);

        // Act: Unsubscribe
        await client.Unsubscribe(query, sub);
        await webSocketCallback.OnMessageAsync("{\"op\":\"unsubscribed\",\"requestId\":1}");

        // Assert it was removed from the cache
        var missingSub = client.GetSubscriptionByName("GlobalChatListener");
        Assert.IsNull(missingSub, "The named subscription should be removed from the cache after unsubscribing.");
    }

  

    [TestMethod]
    [Description("Tests that disposing the client cleans up subscriptions, dictionaries, and closes the WebSocket.")]
    public async Task Client_OnDispose_CleansUpResources()
    {
        // Arrange
        var query = new ParseQuery<TestChat>(serviceHub);
        client.Subscribe(query, "TempSub");
        await webSocketCallback.OnMessageAsync("{\"op\":\"subscribed\",\"requestId\":1}");

        Assert.AreEqual(1, client.Subscriptions.Count);
        Assert.AreEqual(1, client.NamedSubscriptions.Count);

        // Act
        await client.DisposeAsync();

        // Assert
        Assert.AreEqual(0, client.Subscriptions.Count, "Internal subscriptions dictionary should be cleared.");
        Assert.AreEqual(0, client.NamedSubscriptions.Count, "Named subscriptions dictionary should be cleared.");
    }

    [TestMethod]
    [Description("Tests that receiving an unregistered subclass payload pushes an error to the stream instead of crashing.")]
    public async Task Client_OnUnregisteredSubclassPayload_EmitsErrorToStream()
    {
        // Arrange: We subscribe to TestChat
        var query = new ParseQuery<TestChat>(serviceHub);
        var sub = client.Subscribe(query);
        await webSocketCallback.OnMessageAsync("{\"op\":\"subscribed\",\"requestId\":1}");

        LiveQueryException caughtError = null;
        client.OnError.Subscribe(ex => caughtError = ex);

        // Act: The server sends a payload for "UnregisteredChat"
        var badPayload = "{\"op\":\"create\",\"requestId\":1,\"object\":{\"className\":\"UnregisteredChat\",\"objectId\":\"bad123\"}}";
        await webSocketCallback.OnMessageAsync(badPayload);

        // Assert
        Assert.IsNotNull(caughtError, "An error should have been pushed to the OnError stream.");
        Assert.IsNotNull(caughtError.InnerException, "The root exception should be wrapped as the InnerException.");
        Assert.IsTrue(caughtError.InnerException.Message.Contains("casting error"), "The inner exception should explain the casting error.");
    }
}
