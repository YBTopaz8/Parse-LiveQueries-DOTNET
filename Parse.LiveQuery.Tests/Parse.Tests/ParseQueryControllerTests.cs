using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Parse.Abstractions.Infrastructure;
using Parse.Abstractions.Infrastructure.Data;
using Parse.Abstractions.Infrastructure.Execution;
using Parse.Abstractions.Internal;
using Parse.Abstractions.Platform.Objects;
using Parse.Infrastructure;
using Parse.Infrastructure.Execution;
using Parse.Platform.Objects;
using Parse.Platform.Queries;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using YB.Parse.LiveQuery.Parse.Utilities;

namespace Parse.Tests;

[TestClass]
public class ParseQueryControllerTests
{
    private Mock<IParseCommandRunner> mockCommandRunner;
    private IServiceHub serviceHub;

    [TestInitialize]
    public void SetUp()
    {
        mockCommandRunner = new Mock<IParseCommandRunner>();

        var hub = new MutableServiceHub
        {
            CommandRunner = mockCommandRunner.Object
        };
        hub.SetDefaults(); // This correctly initializes all dependencies, including ClassController.

        var client = new ParseClient(new ServerConnectionData { Test = true }, hub);
        serviceHub = client.Services;
    }

    [TestMethod]
    [Description("Tests that FindAsync correctly decodes a list of objects.")]
    public async Task FindAsync_WithResults_ReturnsDecodedStates()
    {
        // Arrange
        var controller = new ParseQueryController(mockCommandRunner.Object, serviceHub.Decoder);
        var query = new ParseQuery<ParseObject>(serviceHub, "TestClass");
        var serverResponse = new Dictionary<string, object>
        {
            ["results"] = new List<object>
                {
                    new Dictionary<string, object> { ["__type"] = "Object", ["className"] = "TestClass", ["objectId"] = "obj1" },
                    new Dictionary<string, object> { ["__type"] = "Object", ["className"] = "TestClass", ["objectId"] = "obj2" }
                }
        };
        var tupleResponse = new Tuple<System.Net.HttpStatusCode, IDictionary<string, object>>(System.Net.HttpStatusCode.OK, serverResponse);

        mockCommandRunner.Setup(r => r.RunCommandAsync(It.IsAny<ParseCommand>(), It.IsAny<IProgress<IDataTransferLevel>>(), It.IsAny<IProgress<IDataTransferLevel>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tupleResponse);

        // Act
        var result = await controller.FindAsync(query, null, CancellationToken.None);

        // Assert
        Assert.AreEqual(2, result.Count());
        Assert.AreEqual("obj1", result.First().ObjectId);
    }

    [TestMethod]
    [Description("Tests that CountAsync returns the correct count from the server response.")]
    public async Task CountAsync_ReturnsCorrectCount()
    {
        // Arrange
        var controller = new ParseQueryController(mockCommandRunner.Object, serviceHub.Decoder);
        var query = new ParseQuery<ParseObject>(serviceHub, "TestClass");
        var serverResponse = new Dictionary<string, object> { ["count"] = 150 };
        var tupleResponse = new Tuple<System.Net.HttpStatusCode, IDictionary<string, object>>(System.Net.HttpStatusCode.OK, serverResponse);

        mockCommandRunner.Setup(r => r.RunCommandAsync(It.IsAny<ParseCommand>(), It.IsAny<IProgress<IDataTransferLevel>>(), It.IsAny<IProgress<IDataTransferLevel>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tupleResponse);

        // Act
        int count = await controller.CountAsync(query, null, CancellationToken.None);

        // Assert
        Assert.AreEqual(150, count);
    }

    [TestMethod]
    [Description("Tests that FirstAsync returns the correctly decoded first object.")]
    public async Task FirstAsync_ReturnsFirstObject()
    {
        // Arrange
        var controller = new ParseQueryController(mockCommandRunner.Object, serviceHub.Decoder);
        var query = new ParseQuery<ParseObject>(serviceHub, "TestClass");
        var serverResponse = new Dictionary<string, object>
        {
            ["results"] = new List<object> { new Dictionary<string, object> { ["__type"] = "Object", ["className"] = "TestClass", ["objectId"] = "theFirst" } }
        };
        var tupleResponse = new Tuple<System.Net.HttpStatusCode, IDictionary<string, object>>(System.Net.HttpStatusCode.OK, serverResponse);

        mockCommandRunner.Setup(r => r.RunCommandAsync(It.IsAny<ParseCommand>(), It.IsAny<IProgress<IDataTransferLevel>>(), It.IsAny<IProgress<IDataTransferLevel>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tupleResponse);

        // Act
        var result = await controller.FirstAsync(query, null, CancellationToken.None);

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual("theFirst", result.ObjectId);
    }

    [TestMethod]
    [Description("Tests that the distinct query correctly constructs the API path and returns a typed list.")]
    public async Task DistinctAsync_ReturnsCorrectlyTypedList()
    {
        // Arrange
        var controller = new ParseQueryController(mockCommandRunner.Object, serviceHub.Decoder);
        var query = new ParseQuery<ParseObject>(serviceHub, "Player");

        // Simulate server returning a list of strings
        var serverResponse = new Dictionary<string, object>
        {
            ["results"] = new List<object> { "Yvan", "Alice", "Bob" }
        };
        var tupleResponse = new Tuple<System.Net.HttpStatusCode, IDictionary<string, object>>(System.Net.HttpStatusCode.OK, serverResponse);

        mockCommandRunner.Setup(r => r.RunCommandAsync(
            It.Is<ParseCommand>(c => c.Path.Contains("aggregate/Player") && c.Path.Contains("distinct=playerName")),
            null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tupleResponse);

        // Act
        var results = await controller.DistinctAsync<ParseObject, string>(query, "playerName", null, CancellationToken.None);

        // Assert
        var list = results.ToList();
        Assert.AreEqual(3, list.Count);
        Assert.AreEqual("Yvan", list[0]);
    }

    [TestMethod]
    [Description("Tests that the Fluent Pipeline Builder correctly generates the MongoDB payload.")]
    public async Task PipelineBuilder_GeneratesCorrectMongoDBPayload()
    {
        // Arrange
        var query = new ParseQuery<ParseObject>(serviceHub, "GameScore");

        // Act: Build a complex pipeline
        var pipelineBuilder = query.Aggregate()
            .Match(new ParseQuery<ParseObject>(serviceHub, "GameScore").WhereGreaterThan("score", 50))
            .Group("playerName",
                ("totalScore", ParseAccumulator.Sum, "score"),
                ("averageScore", ParseAccumulator.Avg, "score")
            )
            .Sort("totalScore", descending: true)
            .Limit(5);

        // We use reflection just for testing to extract the internal pipeline list
        var pipelineField = typeof(ParsePipelineBuilder<ParseObject>).GetField("_pipeline", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var rawPipeline = pipelineField.GetValue(pipelineBuilder) as List<object>;

        // Assert
        Assert.AreEqual(4, rawPipeline.Count); // Match, Group, Sort, Limit

        // Check Group Stage
        var groupStage = (Dictionary<string, object>)rawPipeline[1];
        var groupDict = (Dictionary<string, object>)groupStage["$group"];

        Assert.AreEqual("$playerName", groupDict["_id"]);

        var totalScoreDict = (Dictionary<string, object>)groupDict["totalScore"];
        Assert.AreEqual("$score", totalScoreDict["$sum"]);
    }
}