using Microsoft.VisualStudio.TestTools.UnitTesting;

using Moq;

using Parse.Abstractions.Infrastructure;
using Parse.Infrastructure;
using Parse.LiveQuery.Tests.ParseLiveQueries.Tests;
using Parse.Tests;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

using static Parse.LiveQuery.ParseLiveQueryClient;

namespace Parse.LiveQuery.Tests;
[TestClass]
public class LiveQueryIntegrationScenariosTests
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

        // Register all subclasses that will be used in the tests.
        parseClient.AddValidClass<TestChat>();

        parseClient.Publicize();
        serviceHub = parseClient.Services;

        mockWebSocketFactory.Setup(f => f(It.IsAny<Uri>(), It.IsAny<IWebSocketClientCallback>(), It.IsAny<int>()))
            .Callback<Uri, IWebSocketClientCallback, int>((_, cb, __) => webSocketCallback = cb)
            .Returns(mockWebSocket.Object);

        client = new ParseLiveQueryClient(new Uri("ws://localhost/"), mockWebSocketFactory.Object, new SubscriptionFactory(), new SynchronousTaskQueue());

        client.Start();
        webSocketCallback.OnOpen().Wait();
        webSocketCallback.OnMessage("{\"op\":\"connected\"}").Wait();
    }

    [TestCleanup]
    public void TearDown()
    {
        client?.DisposeAsync();
    }

    [TestMethod]
    [Description("[Chat Scenario] Replicates a user subscribing and receiving a new chat message.")]
    public async Task ChatScenario_WhenNewMessageIsCreated_SubscriberReceivesCreateEvent()
    {
        // ARRANGE
        var chatQuery = new ParseQuery<TestChat>(serviceHub);
        TestChat receivedMessage = new();
        var subscription = client.Subscribe(chatQuery);
        subscription.On(Subscription.Event.Create, (obj) => receivedMessage = obj);
        await webSocketCallback.OnMessage("{\"op\":\"subscribed\",\"requestId\":1}");

        // ACT
        var serverMessage = "{\"op\":\"create\",\"requestId\":1,\"object\":{\"className\":\"TestChat\",\"objectId\":\"msg123\",\"Msg\":\"Hello!\"}}";
        await webSocketCallback.OnMessage(serverMessage);


        // ASSERT
        Assert.IsNotNull(receivedMessage, "A new message should have been received.");
        Assert.AreEqual("msg123", receivedMessage.ObjectId);
        Assert.AreEqual("Hello!", receivedMessage.Msg);
    }

    [TestMethod]
    [Description("[Chat Scenario] Replicates a user receiving an update to an existing chat message.")]
    public async Task ChatScenario_WhenMessageIsUpdated_SubscriberReceivesUpdateEvent()
    {
        // ARRANGE
        var chatQuery = new ParseQuery<TestChat>(serviceHub);
        TestChat updatedMessage = new();
        var subscription = client.Subscribe(chatQuery);
        subscription.On(Subscription.Event.Update, (obj, q) => updatedMessage = obj);
        await webSocketCallback.OnMessage("{\"op\":\"subscribed\",\"requestId\":1}");

        // ACT
        var serverMessage = "{\"op\":\"update\",\"requestId\":1,\"object\":{\"className\":\"TestChat\",\"objectId\":\"msg123\",\"Msg\":\"Updated message!\"}}";
        await webSocketCallback.OnMessage(serverMessage);

        // ASSERT
        Assert.IsNotNull(updatedMessage, "An updated message should have been received.");
        Assert.AreEqual("msg123", updatedMessage.ObjectId);
        Assert.AreEqual("Updated message!", updatedMessage.Msg);
    }

    [TestMethod]
    [Description("[Chat Scenario] Replicates a user receiving a delete event for a chat message.")]
    public async Task ChatScenario_WhenMessageIsDeleted_SubscriberReceivesDeleteEvent()
    {
        // ARRANGE
        var chatQuery = new ParseQuery<TestChat>(serviceHub);
        TestChat deletedMessage = new();
        var subscription = client.Subscribe(chatQuery);
        subscription.On(Subscription.Event.Delete, (obj) => deletedMessage = obj);
        await webSocketCallback.OnMessage("{\"op\":\"subscribed\",\"requestId\":1}");

        // ACT
        var serverMessage = "{\"op\":\"delete\",\"requestId\":1,\"object\":{\"className\":\"TestChat\",\"objectId\":\"msg123\"}}";
        await webSocketCallback.OnMessage(serverMessage);

        // ASSERT
        Assert.IsNotNull(deletedMessage, "A delete event should have been received.");
        Assert.AreEqual("msg123", deletedMessage.ObjectId);
    }

    [TestMethod]
    [Description("[Friend Request Scenario] Simulates subscribing to friend requests and receiving one.")]
    public async Task FriendRequest_WhenRequestIsCreated_SubscriberReceivesEvent()
    {
        // ARRANGE
        var currentUser = serviceHub.CreateObjectWithoutData<ParseUser>("currentUser_id");
        var requestQuery = new ParseQuery<ParseObject>(serviceHub, "FriendRequest")
            .WhereEqualTo("recipient", currentUser);
        ParseObject receivedRequest = serviceHub.CreateObjectWithoutData<ParseObject>("currentUser_id");

        var subscription = client.Subscribe(requestQuery);
        subscription.On(Subscription.Event.Create, (obj, q) => receivedRequest = obj);
        await webSocketCallback.OnMessage("{\"op\":\"subscribed\",\"requestId\":1}");

        // ACT
        var serverMessage = "{\"op\":\"create\",\"requestId\":1,\"object\":{\"className\":\"FriendRequest\",\"objectId\":\"req123\",\"sender\":{\"__type\":\"Pointer\",\"className\":\"_User\",\"objectId\":\"otherUser_id\"}}}";
        await webSocketCallback.OnMessage(serverMessage);

        // ASSERT
        Assert.IsNotNull(receivedRequest, "A friend request should have been received.");
        Assert.AreEqual("req123", receivedRequest.ObjectId);
        var sender = receivedRequest.Get<ParseUser>("sender");
        Assert.AreEqual("otherUser_id", sender.ObjectId);
    }

    [TestMethod]
    [Description("[Remote Control Scenario] Simulates a phone receiving a player state update.")]
    public async Task RemoteControl_WhenStateChanges_SubscriberReceivesUpdate()
    {
        // ARRANGE
        var playerStateQuery = new ParseQuery<ParseObject>(serviceHub, "PlayerState")
            .WhereEqualTo("objectId", "playerState_abc");
        ParseObject updatedState = serviceHub.CreateObjectWithoutData<ParseObject>("currentUser_id");

        var subscription = client.Subscribe(playerStateQuery);
        subscription.On(Subscription.Event.Update, (obj, q) => updatedState = obj);
        await webSocketCallback.OnMessage("{\"op\":\"subscribed\",\"requestId\":1}");

        // ACT: The desktop app plays the next song.
        var serverMessage = "{\"op\":\"update\",\"requestId\":1,\"object\":{\"className\":\"PlayerState\",\"objectId\":\"playerState_abc\",\"currentTrack\":\"New Song Title\",\"isPlaying\":true}}";
        await webSocketCallback.OnMessage(serverMessage);

        // ASSERT
        Assert.IsNotNull(updatedState, "The player state should have been updated.");
        Assert.AreEqual("New Song Title", updatedState.Get<string>("currentTrack"));
        Assert.IsTrue(updatedState.Get<bool>("isPlaying"));
    }

}








[ParseClassName("TestChat")]
public partial class TestChat : ParseObject
{

    [ParseFieldName("Msg")]

    public string? Msg // Nullable string
    {
        get => Get<string?>(nameof(Msg)); // Get<string> will often return null if not set
        set => Set(nameof(Msg), value);
    }

    [ParseFieldName("Username")]

    public string? Username // Nullable string
    {
        get => Get<string?>(nameof(Username));
        set => Set(nameof(Username), value);
    }


    [ParseFieldName("IsDeleted")]

    public bool IsDeleted
    {
        get => Get<bool>(nameof(IsDeleted)); // Booleans default to false if not set in Parse
        set => Set(nameof(IsDeleted), value);
    }
}