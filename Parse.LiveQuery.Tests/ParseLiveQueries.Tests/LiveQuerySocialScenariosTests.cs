using Microsoft.VisualStudio.TestTools.UnitTesting;

using Moq;

using Parse.Abstractions.Infrastructure;
using Parse.Abstractions.Infrastructure.Execution;
using Parse.Infrastructure;
using Parse.Infrastructure.Execution;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;

using static Parse.LiveQuery.ParseLiveQueryClient;

namespace Parse.LiveQuery.Tests.ParseLiveQueries.Tests;
[TestClass]
public class LiveQuerySocialScenariosTests
{
    private ParseLiveQueryClient clientA; // Client for User A
    private IWebSocketClientCallback callbackA;
    private IServiceHub serviceHub;
    private Mock<IParseCommandRunner> mockCommandRunner;
    [TestInitialize]
    public void SetUp()
    {
        var mockWebSocketFactory = new Mock<WebSocketClientFactory>();
        var mockWebSocket = new Mock<IWebSocketClient>();
        mockCommandRunner = new Mock<IParseCommandRunner>();
      
        var hub = new MutableServiceHub { CommandRunner = mockCommandRunner.Object };
        hub.SetDefaults();
        var parseClient = new ParseClient(new ServerConnectionData { ApplicationID = "appId", Key = "dotnetKey", ServerURI = "http://localhost/" }, hub);

        parseClient.AddValidClass<TestChat>();
        parseClient.AddValidClass<ParseUser>();
        parseClient.Publicize();
        serviceHub = parseClient.Services;

        // We need to set up a default response for the FindAsync call that happens
        // inside the test's ARRANGE block.
        var emptyResponse = new Dictionary<string, object> { ["results"] = new List<object>() };
        var tupleResponse = new Tuple<System.Net.HttpStatusCode, IDictionary<string, object>>(System.Net.HttpStatusCode.OK, emptyResponse);

        // Tell the mock: "If you get ANY command, just return a successful empty response."
        // This is a simple way to handle all setup queries.
        mockCommandRunner.Setup(r => r.RunCommandAsync(It.IsAny<ParseCommand>(), It.IsAny<IProgress<IDataTransferLevel>>(), It.IsAny<IProgress<IDataTransferLevel>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tupleResponse);

        mockWebSocketFactory.Setup(f => f(It.IsAny<Uri>(), It.IsAny<IWebSocketClientCallback>(), It.IsAny<int>()))
            .Callback<Uri, IWebSocketClientCallback, int>((_, cb, __) => callbackA = cb)
            .Returns(mockWebSocket.Object);

        clientA = new ParseLiveQueryClient(new Uri("ws://localhost/"), mockWebSocketFactory.Object, new SubscriptionFactory(), new SynchronousTaskQueue());

        clientA.Start();

        callbackA.OnOpen().Wait();
        callbackA.OnMessage("{\"op\":\"connected\"}").Wait();
    }

    [TestCleanup]
    public void TearDown()
    {
        clientA?.Dispose();
    }

    [TestMethod]
    [Description("[Full Scenario] Tests the entire friend request, acceptance, and first message flow.")]
    public async Task FullSocialFlow_FriendRequestIsSentAndAccepted_FirstMessageIsReceived()
    {
        // ===== ARRANGE: User A comes online and sets up all subscriptions =====

        var userA = serviceHub.CreateObjectWithoutData<ParseUser>("userA_id");

        // 1. User A subscribes to incoming friend requests.
        var friendRequestQuery = new ParseQuery<ParseObject>(serviceHub, "FriendRequest").WhereEqualTo("recipient", userA);
        ParseObject receivedRequest = null;
        var requestSubscription = await clientA.SubscribeAsync(friendRequestQuery);
        requestSubscription.On(Subscription.Event.Create, (obj, q) => receivedRequest = obj);
        await callbackA.OnMessage("{\"op\":\"subscribed\",\"requestId\":1}");

        // 2. User A subscribes to new GroupChats they are a member of.
        var groupChatQuery = new ParseQuery<ParseObject>(serviceHub, "GroupChat").WhereEqualTo("members", userA);
        ParseObject newGroupChat = null;
        var groupSubscription = await clientA.SubscribeAsync(groupChatQuery);
        groupSubscription.On(Subscription.Event.Create, (obj, q) => newGroupChat = obj);
        await callbackA.OnMessage("{\"op\":\"subscribed\",\"requestId\":2}");

        // 3. User A sets up a combined, throttled subscription for new messages in ALL of their chats.
        //    This is an advanced Rx.NET test!
        var messageQuery = new ParseQuery<TestChat>(serviceHub).WhereContainedIn("parentChat", await groupChatQuery.FindAsync()); // Hypothetical query

        var receivedMessages = new List<TestChat>();
        var messageSubscription = await clientA.SubscribeAsync(messageQuery);
        messageSubscription.Events
            .Where(e => e.EventType == Subscription.Event.Create)
            .Select(e => e.Object)
            .Buffer(TimeSpan.FromSeconds(1), 5) // Buffer messages for 1 second or until 5 arrive
            .Subscribe(batch =>
            {
                receivedMessages.AddRange(batch);
            });
        await callbackA.OnMessage("{\"op\":\"subscribed\",\"requestId\":3}");


        // ===== ACT 1: User B sends a friend request =====
        var serverMsg_FriendRequest = "{\"op\":\"create\",\"requestId\":1,\"object\":{\"className\":\"FriendRequest\",\"objectId\":\"req123\",\"sender\":{\"__type\":\"Pointer\",\"className\":\"_User\",\"objectId\":\"userB_id\"}}}";
        await callbackA.OnMessage(serverMsg_FriendRequest);

        // ===== ASSERT 1: User A receives the friend request =====
        Assert.IsNotNull(receivedRequest, "User A should have received the friend request via LiveQuery.");
        Assert.AreEqual("req123", receivedRequest.ObjectId);


        // ===== ACT 2: User A "accepts" the request (simulated Cloud Function call) =====
        // The server would create a new GroupChat and notify User A.
        var serverMsg_NewGroupChat = "{\"op\":\"create\",\"requestId\":2,\"object\":{\"className\":\"GroupChat\",\"objectId\":\"group456\",\"members\":[{\"__type\":\"Pointer\",\"className\":\"_User\",\"objectId\":\"userA_id\"},{\"__type\":\"Pointer\",\"className\":\"_User\",\"objectId\":\"userB_id\"}]}}";
        await callbackA.OnMessage(serverMsg_NewGroupChat);

        // ===== ASSERT 2: User A receives the new group chat =====
        Assert.IsNotNull(newGroupChat, "User A should have been notified of the new group chat.");
        Assert.AreEqual("group456", newGroupChat.ObjectId);


        // ===== ACT 3: User B sends the first message to the new group chat =====
        var serverMsg_FirstMessage = "{\"op\":\"create\",\"requestId\":3,\"object\":{\"className\":\"TestChat\",\"objectId\":\"msg789\",\"text\":\"Hey, we're friends now!\",\"parentChat\":{\"__type\":\"Pointer\",\"className\":\"GroupChat\",\"objectId\":\"group456\"}}}";
        await callbackA.OnMessage(serverMsg_FirstMessage);

        // Give the Buffer/Throttle time to work
        await Task.Delay(1100);

        // ===== ASSERT 3: User A receives the new message through their combined subscription =====
        Assert.AreEqual(1, receivedMessages.Count, "User A should have received one new message.");
        Assert.AreEqual("msg789", receivedMessages[0].ObjectId);
    }
}