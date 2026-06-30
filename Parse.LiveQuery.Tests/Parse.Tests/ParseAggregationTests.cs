using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Parse.Abstractions.Infrastructure.Execution;
using Parse.Infrastructure;
using Parse.Infrastructure.Execution;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using YB.Parse.LiveQuery.Parse.Utilities;

namespace Parse.Tests;

[TestClass]
public class ParseAggregationTests
{
    private ParseClient Client { get; set; }
    private Mock<IParseCommandRunner> MockRunner { get; set; }

    [TestInitialize]
    public void SetUp()
    {
        MockRunner = new Mock<IParseCommandRunner>();
        var hub = new MutableServiceHub { CommandRunner = MockRunner.Object };
        hub.SetDefaults();


        Client = new ParseClient(new ServerConnectionData
        {
            Test = true,
            ServerURI = "http://localhost:1337/parse/"
        }, hub);
        Client.Publicize();
    }

    [TestMethod]
    [Description("[Scenario: Sales Dashboard] Calculates total revenue and average order value for a specific user.")]
    public async Task Aggregate_WithMultipleAccumulators_GeneratesCorrectPayload()
    {
        var query = new ParseQuery<ParseObject>(Client.Services, "Order");

        // We simulate calling ExecuteAsync, so we mock the runner
        MockRunner.Setup(r => r.RunCommandAsync(It.IsAny<ParseCommand>(), null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tuple<System.Net.HttpStatusCode, IDictionary<string, object>>(
                System.Net.HttpStatusCode.OK, new Dictionary<string, object> { ["results"] = new List<object>() }
            ));

        await query.Aggregate()
            .Match(new ParseQuery<ParseObject>(Client.Services, "Order").WhereEqualTo("status", "completed"))
            .Group("sellerId",
                ("TotalRevenue", ParseAccumulator.Sum, "price"),
                ("AverageOrder", ParseAccumulator.Avg, "price"),
                ("HighestSale", ParseAccumulator.Max, "price"),
                ("LowestSale", ParseAccumulator.Min, "price")
            )
            .ExecuteAsync();

        // Verify the exact JSON structure sent to the server
        MockRunner.Verify(r => r.RunCommandAsync(It.Is<ParseCommand>(cmd =>
            cmd.Path.Contains("aggregate/Order") &&
            cmd.Path.Contains("%24sum") &&
            cmd.Path.Contains("%24avg") &&
            cmd.Path.Contains("%24max") &&
            cmd.Path.Contains("%24min")
        ), null, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    [Description("[Scenario: Global Metrics] Grouping by NULL to calculate a total across the ENTIRE collection.")]
    public void Aggregate_GroupByNull_CalculatesGlobalTotal()
    {
        var query = new ParseQuery<ParseObject>(Client.Services, "Player");
        var builder = query.Aggregate().Group(null, ("GlobalPlayerCount", ParseAccumulator.Sum, "1"));

        var pipelineField = typeof(ParsePipelineBuilder<ParseObject>).GetField("_pipeline", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var rawPipeline = pipelineField.GetValue(builder) as List<object>;

        var groupStage = (Dictionary<string, object>)rawPipeline[0];
        var groupDict = (Dictionary<string, object>)groupStage["$group"];

        // In MongoDB, grouping by null aggregates all documents into a single result
        Assert.IsNull(groupDict["_id"]);

        var sumDict = (Dictionary<string, object>)groupDict["GlobalPlayerCount"];
        Assert.AreEqual(1, sumDict["$sum"], "Summing by 1 is the standard way to count rows in an aggregation.");
    }

    [TestMethod]
    [Description("[Scenario: Analytics] Extracts unique items into an array using AddToSet and Push.")]
    public void Aggregate_PushAndAddToSet_GeneratesArrayAccumulators()
    {
        var query = new ParseQuery<ParseObject>(Client.Services, "Log");
        var builder = query.Aggregate().Group("deviceId",
            ("AllEvents", ParseAccumulator.Push, "eventType"),
            ("UniqueEvents", ParseAccumulator.AddToSet, "eventType")
        );

        var pipelineField = typeof(ParsePipelineBuilder<ParseObject>).GetField("_pipeline", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var rawPipeline = pipelineField.GetValue(builder) as List<object>;

        var groupDict = (Dictionary<string, object>)((Dictionary<string, object>)rawPipeline[0])["$group"];

        var pushDict = (Dictionary<string, object>)groupDict["AllEvents"];
        Assert.AreEqual("$eventType", pushDict["$push"]);

        var addToSetDict = (Dictionary<string, object>)groupDict["UniqueEvents"];
        Assert.AreEqual("$eventType", addToSetDict["$addToSet"]);
    }

    [TestMethod]
    [Description("[Scenario: Pagination] Tests Skip, Limit, and Project in sequence.")]
    public void Aggregate_PaginationAndProjection_BuildsStagesInCorrectOrder()
    {
        var query = new ParseQuery<ParseObject>(Client.Services, "Log");
        var builder = query.Aggregate()
            .Skip(100)
            .Limit(50)
            .Project("deviceId", "timestamp");

        var pipelineField = typeof(ParsePipelineBuilder<ParseObject>).GetField("_pipeline", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var rawPipeline = pipelineField.GetValue(builder) as List<object>;

        Assert.AreEqual(3, rawPipeline.Count);
        Assert.IsTrue(((Dictionary<string, object>)rawPipeline[0]).ContainsKey("$skip"));
        Assert.IsTrue(((Dictionary<string, object>)rawPipeline[1]).ContainsKey("$limit"));
        Assert.IsTrue(((Dictionary<string, object>)rawPipeline[2]).ContainsKey("$project"));
    }




    // Make a dummy class to test the [ParseFieldName] mapping
    [ParseClassName("AggTestClass")]
    private class AggTestClass : ParseObject
    {
        [ParseFieldName("revenue")]
        public double Revenue { get; set; }

        [ParseFieldName("user_id")]
        public string UserId { get; set; }

        [ParseFieldName("is_active")]
        public bool IsActive { get; set; }
    }



    [TestMethod]
    [Description("Tests that the strongly-typed Group method resolves [ParseFieldName] correctly.")]
    public void Aggregate_StronglyTypedGroup_GeneratesCorrectFieldNames()
    {
        var query = new ParseQuery<AggTestClass>(Client.Services, "AggTestClass");

        // Act using the Expression lambda x => x.UserId
        var builder = query.Aggregate()
            .Group(x => x.UserId, ("TotalRevenue", ParseAccumulator.Sum, "revenue"));

        // Extract the raw dictionary to verify the internal build
        var pipelineField = typeof(ParsePipelineBuilder<AggTestClass>).GetField("_pipeline", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var rawPipeline = pipelineField.GetValue(builder) as List<object>;

        var groupStage = (Dictionary<string, object>)rawPipeline[0];
        var groupDict = (Dictionary<string, object?>)groupStage["$group"];

        // Assert that x => x.UserId was correctly translated to "$user_id"
        Assert.AreEqual("$user_id", groupDict["_id"], "The lambda expression should map to the [ParseFieldName] with a $ prefix.");
    }

    [TestMethod]
    [Description("Tests that strongly-typed Project and Sort resolve [ParseFieldName] correctly.")]
    public void Aggregate_StronglyTypedProjectAndSort_GeneratesCorrectFieldNames()
    {
        var query = new ParseQuery<AggTestClass>(Client.Services, "AggTestClass");

        // Act using Expression lambdas
        var builder = query.Aggregate()
            .Project(x => x.UserId, x => x.IsActive)
            .Sort(x => x.Revenue, descending: true);

        // Extract the raw dictionary
        var pipelineField = typeof(ParsePipelineBuilder<AggTestClass>).GetField("_pipeline", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var rawPipeline = pipelineField.GetValue(builder) as List<object>;

        // 1. Verify Project
        var projectStage = (Dictionary<string, object>)rawPipeline[0];
        var projectDict = (Dictionary<string, object>)projectStage["$project"];

        Assert.IsTrue(projectDict.ContainsKey("user_id"), "Project should resolve x.UserId to 'user_id'");
        Assert.IsTrue(projectDict.ContainsKey("is_active"), "Project should resolve x.IsActive to 'is_active'");

        // 2. Verify Sort
        var sortStage = (Dictionary<string, object>)rawPipeline[1];
        var sortDict = (Dictionary<string, object>)sortStage["$sort"];

        Assert.IsTrue(sortDict.ContainsKey("revenue"), "Sort should resolve x.Revenue to 'revenue'");
        Assert.AreEqual(-1, sortDict["revenue"], "Descending sort should equal -1");
    }
}