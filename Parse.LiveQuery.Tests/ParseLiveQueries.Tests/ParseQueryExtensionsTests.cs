using Microsoft.VisualStudio.TestTools.UnitTesting;

using Moq;

using Parse.Abstractions.Infrastructure;
using Parse.Infrastructure;
using Parse.LiveQuery.Tests.ParseLiveQueries.Tests;

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
        client?.Dispose();
        typeof(ParseClient).GetField("Instance", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).SetValue(null, null);
    }

    [TestMethod]
    [Description("Simulates User A subscribing and User B sending a friend request.")]
    public async Task FriendRequest_UserB_SendsRequest_UserA_ReceivesCreateEvent()
    {
        // ARRANGE
        var userA = serviceHub.CreateObjectWithoutData<ParseUser>("userA_id");
        var friendRequestQuery = new ParseQuery<ParseObject>(serviceHub, "FriendRequest")
            .WhereEqualTo("recipient", userA);

        ParseObject receivedRequest = null;
        var subscription = await client.SubscribeAsync(friendRequestQuery);
        subscription.On(Subscription.Event.Create, (obj, q) => receivedRequest = obj);
        await webSocketCallback.OnMessage("{\"op\":\"subscribed\",\"requestId\":1}");

        // ACT
        var serverMessage = "{\"op\":\"create\",\"requestId\":1,\"object\":{\"className\":\"FriendRequest\",\"objectId\":\"req123\",\"sender\":{\"__type\":\"Pointer\",\"className\":\"_User\",\"objectId\":\"userB_id\"}}}";
        await webSocketCallback.OnMessage(serverMessage);

        // ASSERT
        Assert.IsNotNull(receivedRequest, "User A should have received the friend request.");
        Assert.AreEqual("req123", receivedRequest.ObjectId);
    }

    [TestMethod]
    [Description("Simulates two users subscribing to a chat and exchanging messages.")]
    public async Task DirectMessage_UsersExchangeMessages_ReceiveUpdateEvents()
    {
        // ARRANGE
        var chatSessionQuery = new ParseQuery<ParseObject>(serviceHub, "ChatSession")
            .WhereEqualTo("objectId", "chat456");

        ParseObject updatedChat = null;
        var subscription = await client.SubscribeAsync(chatSessionQuery);
        subscription.On(Subscription.Event.Update, (obj, q) => updatedChat = obj);
        await webSocketCallback.OnMessage("{\"op\":\"subscribed\",\"requestId\":1}");

        // ACT 1: User A sends a message.
        var messageFromA = "{\"op\":\"update\",\"requestId\":1,\"object\":{\"className\":\"ChatSession\",\"objectId\":\"chat456\",\"lastMessage\":\"Hello!\"}}";
        Debug.WriteLine("TEST: Sending 'update' message to callback...");
        await webSocketCallback.OnMessage(messageFromA);
        Debug.WriteLine("TEST: 'update' message sent.");


        // ASSERT
        Assert.IsNotNull(updatedChat);
        Assert.AreEqual("Hello!", updatedChat.Get<string>("lastMessage"));
    }

    [TestMethod]
    [Description("Simulates a user entering a group chat and receiving a message.")]
    public async Task GroupChat_UserEntersAndReceivesMessage()
    {
        // ARRANGE
        var groupChat = serviceHub.CreateObjectWithoutData("GroupChat", "group1");
        var messageQuery = new ParseQuery<ParseObject>(serviceHub, "Message")
            .WhereEqualTo("group", groupChat);

        var receivedMessages = new List<ParseObject>();
        var subscription = await client.SubscribeAsync(messageQuery);
        subscription.On(Subscription.Event.Enter, (obj, q) => receivedMessages.Add(obj));
        await webSocketCallback.OnMessage("{\"op\":\"subscribed\",\"requestId\":1}");

        // ACT
        var serverMessage = "{\"op\":\"enter\",\"requestId\":1,\"object\":{\"className\":\"Message\",\"objectId\":\"msg1\",\"text\":\"Welcome!\"}}";
        await webSocketCallback.OnMessage(serverMessage);

        // ASSERT
        Assert.AreEqual(1, receivedMessages.Count, "Should have received one message.");
        Assert.AreEqual("Welcome!", receivedMessages[0].Get<string>("text"));
    }
}