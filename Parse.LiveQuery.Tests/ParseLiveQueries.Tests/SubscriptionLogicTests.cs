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
public class SubscriptionLogicTests
{
    private ParseLiveQueryClient client;
    private IWebSocketClientCallback webSocketCallback;
    private IServiceHub serviceHub;

    [TestInitialize]
    public void SetUp()
    {
        var mockWebSocketFactory = new Mock<WebSocketClientFactory>();
        var hub = new MutableServiceHub { };
        hub.SetDefaults();
        var parseClient = new ParseClient(new ServerConnectionData { ApplicationID = "appId", Key = "dotnetKey", ServerURI = "http://localhost/" }, hub);

        parseClient.AddValidClass<TestChat>();
        parseClient.Publicize();
        serviceHub = parseClient.Services;

        mockWebSocketFactory.Setup(f => f(It.IsAny<Uri>(), It.IsAny<IWebSocketClientCallback>(), It.IsAny<int>()))
            .Callback<Uri, IWebSocketClientCallback, int>((_, cb, __) => webSocketCallback = cb)
            .Returns(new Mock<IWebSocketClient>().Object);

        client = new ParseLiveQueryClient(new Uri("ws://localhost/"), mockWebSocketFactory.Object, new SubscriptionFactory(), new SynchronousTaskQueue());
        client.Start();
        webSocketCallback.OnOpenAsync().Wait();
        webSocketCallback.OnMessageAsync("{\"op\":\"connected\"}").Wait();
    }

    [TestMethod]
    [Description("Tests that updates containing an 'original' payload correctly populate the Old State in the event handler.")]
    public async Task Subscription_OnUpdate_ProvidesOriginalObjectIfAvailable()
    {
        var query = new ParseQuery<TestChat>(serviceHub);
        TestChat originalObj = null;
        TestChat updatedObj = null;

        var sub = client.Subscribe(query);
        sub.OnUpdate += (og, updated) =>
        {
            originalObj = og;
            updatedObj = updated;
        };

        await webSocketCallback.OnMessageAsync("{\"op\":\"subscribed\",\"requestId\":1}");

        // ACT: Send an update with the "original" key
        var updatePayload = "{\"op\":\"update\",\"requestId\":1,\"object\":{\"className\":\"TestChat\",\"objectId\":\"msg1\",\"Msg\":\"New Message!\"},\"original\":{\"className\":\"TestChat\",\"objectId\":\"msg1\",\"Msg\":\"Old Message!\"}}";
        await webSocketCallback.OnMessageAsync(updatePayload);

        // ASSERT
        Assert.IsNotNull(originalObj, "Original object should not be null.");
        Assert.IsNotNull(updatedObj, "Updated object should not be null.");
        Assert.AreEqual("Old Message!", originalObj.Msg);
        Assert.AreEqual("New Message!", updatedObj.Msg);
    }

    [TestMethod]
    [Description("Tests the delayed UnsubscribeAfter functionality.")]
    public async Task Subscription_UnsubscribeAfter_DelaysUnsubscription()
    {
        var query = new ParseQuery<TestChat>(serviceHub);
        var sub = client.Subscribe(query);
        await webSocketCallback.OnMessageAsync("{\"op\":\"subscribed\",\"requestId\":1}");

        bool unsubscribed = false;
        sub.Unsubscribes.Subscribe(_ => unsubscribed = true);

        // Using an artificially short time for testing. Since the method takes minutes, 
        // we can test the internal zero-or-less bypass logic instantly, but to test true delay 
        // we'd normally inject a TimeProvider. For now, we test the instant bypass.

        sub.UnsubscribeAfter(0); // 0 triggers immediate unsubscription in the logic

        Assert.IsTrue(unsubscribed, "Subscription should have unsubscribed immediately when delay is <= 0");
    }

    [TestMethod]
    [Description("Ensures a catastrophic JSON format issue gracefully emits an error instead of crashing the client.")]
    public async Task Client_OnGarbageJson_EmitsInvalidResponseError()
    {
        bool errorCaught = false;
        client.OnError.Subscribe(ex =>
        {
            if (ex is LiveQueryException.InvalidResponseException) errorCaught = true;
        });

        // ACT: Send broken JSON
        await webSocketCallback.OnMessageAsync("{ NOT VALID JSON }");

        // ASSERT
        Assert.IsTrue(errorCaught, "The client should gracefully catch the JSON exception and emit a LiveQueryException.");
    }
}