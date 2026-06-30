using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Parse.Abstractions.Infrastructure;
using Parse.Infrastructure;
using Parse.Infrastructure.Utilities;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Parse.Tests;

[TestClass]
public class ParseQueryTests
{
    private ParseClient Client { get; set; }
    Mock<IServiceHub> MockHub { get; set; }

    [TestInitialize]
    public void SetUp()
    {
        Client = new ParseClient(new ServerConnectionData { Test = true });
        Client.Publicize();
        MockHub = new Mock<IServiceHub>();
        Client.Services = MockHub.Object;
    }
    [TestCleanup]
    public void TearDown()
    {
        if (Client?.Services is OrchestrationServiceHub orchestration && orchestration.Default is ServiceHub serviceHub)
        {
            serviceHub.Reset();
        }
    }

    [TestMethod]
    [Description("Tests constructor, that classes are instantiated correctly.")]
    public void Constructor_CreatesObjectCorrectly() // Mock difficulty: 1
    {
        var query = new ParseQuery<ParseObject>(MockHub.Object, "test");

        Assert.IsNotNull(query.ClassName);
        Assert.IsNotNull(query.Services);
        Assert.ThrowsException<ArgumentNullException>(() => new ParseQuery<ParseObject>(MockHub.Object, null));
    }

    [TestMethod]
    [Description("Tests that ThenBy throws exception if there is no orderby set before hand.")]
    public void ThenBy_ThrowsIfNotSetOrderBy()// Mock difficulty: 1
    {
        var query = new ParseQuery<ParseObject>(MockHub.Object, "test");
        Assert.ThrowsException<ArgumentException>(() => query.ThenBy("test"));

    }

    [TestMethod]
    [Description("Tests that where contains correctly constructs the query for given values")]
    public void WhereContains_SetsRegexSearchValue()// Mock difficulty: 1
    {
        var query = new ParseQuery<ParseObject>(MockHub.Object, "test").WhereContains("test", "test");
        var results = query.GetConstraint("test") as IDictionary<string, object>;
        Assert.IsTrue(results.ContainsKey("$regex"));
        Assert.AreEqual("\\Qtest\\E", results["$regex"]);
    }

    [TestMethod]
    [Description("Tests WhereDoesNotExist correctly builds query")]
    public void WhereDoesNotExist_SetsNewWhereWithDoesNotExist()// Mock difficulty: 1
    {
        var query = new ParseQuery<ParseObject>(MockHub.Object, "test").WhereDoesNotExist("test");
        var results = query.GetConstraint("test") as IDictionary<string, object>;
        Assert.IsTrue(results.ContainsKey("$exists"));
        Assert.AreEqual(false, results["$exists"]);

    }


    [TestMethod]
    [Description("Test WhereEndsWith correctly set query.")]
    public void WhereEndsWith_SetsCorrectRegexEnd()// Mock difficulty: 1
    {
        var query = new ParseQuery<ParseObject>(MockHub.Object, "test").WhereEndsWith("test", "test");
        var results = query.GetConstraint("test") as IDictionary<string, object>;
        Assert.IsTrue(results.ContainsKey("$regex"));
        Assert.AreEqual("\\Qtest\\E$", results["$regex"]);
    }

    [TestMethod]
    [Description("Tests WhereEqualTo correctly builds the query.")]
    public void WhereEqualTo_SetsKeyValueOnWhere() // Mock difficulty: 1
    {
        var query = new ParseQuery<ParseObject>(MockHub.Object, "test").WhereEqualTo("test", "value");
        Assert.AreEqual("value", query.GetConstraint("test"));
    }
    [TestMethod]
    [Description("Tests WhereExists correctly builds query.")]
    public void WhereExists_SetsKeyValueOnWhere()// Mock difficulty: 1
    {
        var query = new ParseQuery<ParseObject>(MockHub.Object, "test").WhereExists("test");
        var results = query.GetConstraint("test") as IDictionary<string, object>;
        Assert.IsTrue(results.ContainsKey("$exists"));
        Assert.AreEqual(true, results["$exists"]);
    }

    [TestMethod]
    [Description("Tests WhereGreaterThan correctly builds the query.")]
    public void WhereGreaterThan_SetsLowerBound()// Mock difficulty: 1
    {
        var query = new ParseQuery<ParseObject>(MockHub.Object, "test").WhereGreaterThan("test", 10);
        var results = query.GetConstraint("test") as IDictionary<string, object>;
        Assert.IsTrue(results.ContainsKey("$gt"));
        Assert.AreEqual(10, results["$gt"]);
    }

    [TestMethod]
    [Description("Tests where greater or equal than sets lower bound properly")]
    public void WhereGreaterThanOrEqualTo_SetsLowerBound()// Mock difficulty: 1
    {
        var query = new ParseQuery<ParseObject>(MockHub.Object, "test").WhereGreaterThanOrEqualTo("test", 10);
        var results = query.GetConstraint("test") as IDictionary<string, object>;
        Assert.IsTrue(results.ContainsKey("$gte"));
        Assert.AreEqual(10, results["$gte"]);
    }
    [TestMethod]
    [Description("Tests if WhereLessThan correctly build the query")]
    public void WhereLessThan_SetsLowerBound()// Mock difficulty: 1
    {
        var query = new ParseQuery<ParseObject>(MockHub.Object, "test").WhereLessThan("test", 10);
        var results = query.GetConstraint("test") as IDictionary<string, object>;
        Assert.IsTrue(results.ContainsKey("$lt"));
        Assert.AreEqual(10, results["$lt"]);

    }

    [TestMethod]
    [Description("Tests where less than or equal to sets query properly")]
    public void WhereLessThanOrEqualTo_SetsLowerBound()// Mock difficulty: 1
    {
        var query = new ParseQuery<ParseObject>(MockHub.Object, "test").WhereLessThanOrEqualTo("test", 10);
        var results = query.GetConstraint("test") as IDictionary<string, object>;
        Assert.IsTrue(results.ContainsKey("$lte"));
        Assert.AreEqual(10, results["$lte"]);
    }
    [TestMethod]
    [Description("Tests if WhereMatches builds query using regex and modifiers correctly")]
    public void WhereMatches_SetsRegexAndModifiersCorrectly()// Mock difficulty: 1
    {
        var regex = new Regex("test", RegexOptions.ECMAScript | RegexOptions.IgnoreCase);
        var query = new ParseQuery<ParseObject>(MockHub.Object, "test").WhereMatches("test", regex, "im");
        var results = query.GetConstraint("test") as IDictionary<string, object>;

        Assert.IsTrue(results.ContainsKey("$regex"));
        Assert.IsTrue(results.ContainsKey("$options"));
        Assert.AreEqual("test", results["$regex"]);
        Assert.AreEqual("im", results["$options"]);
    }

    [TestMethod]
    [Description("Tests if exception is throw on Regex doesn't have proper flags.")]
    public void WhereMatches_RegexWithoutFlag_Throws()// Mock difficulty: 1
    {
        var regex = new Regex("test");
        var query = new ParseQuery<ParseObject>(MockHub.Object, "test");
        Assert.ThrowsException<ArgumentException>(() => query.WhereMatches("test", regex, null));

    }

    [TestMethod]
    [Description("Tests if WhereNear builds query with $nearSphere property.")]
    public void WhereNear_CreatesQueryNearValue()// Mock difficulty: 1
    {
        var point = new ParseGeoPoint(1, 2);
        var query = new ParseQuery<ParseObject>(MockHub.Object, "test").WhereNear("test", point);
        var result = query.GetConstraint("test") as IDictionary<string, object>;
        Assert.IsTrue(result.ContainsKey("$nearSphere"));
        Assert.AreEqual(point, result["$nearSphere"]);

    }

    [TestMethod]
    [Description("Tests WhereNotEqualTo correctly builds the query.")]
    public void WhereNotEqualTo_SetsValueOnWhere()// Mock difficulty: 1
    {
        var query = new ParseQuery<ParseObject>(MockHub.Object, "test").WhereNotEqualTo("test", "value");
        var results = query.GetConstraint("test") as IDictionary<string, object>;
        Assert.IsTrue(results.ContainsKey("$ne"));
        Assert.AreEqual("value", results["$ne"]);
    }

    [TestMethod]
    [Description("Tests where starts with sets regex values")]
    public void WhereStartsWith_SetsCorrectRegexValue()// Mock difficulty: 1
    {
        var query = new ParseQuery<ParseObject>(MockHub.Object, "test").WhereStartsWith("test", "test");
        var results = query.GetConstraint("test") as IDictionary<string, object>;
        Assert.IsTrue(results.ContainsKey("$regex"));
        Assert.AreEqual("^\\Qtest\\E", results["$regex"]);
    }
    [TestMethod]
    [Description("Tests if WhereWithinGeoBox builds query with the correct values")]
    public void WhereWithinGeoBox_SetsWithingValues()// Mock difficulty: 1
    {
        var point1 = new ParseGeoPoint(1, 2);
        var point2 = new ParseGeoPoint(3, 4);
        var query = new ParseQuery<ParseObject>(MockHub.Object, "test").WhereWithinGeoBox("test", point1, point2);
        var results = query.GetConstraint("test") as IDictionary<string, object>;
        Assert.IsTrue(results.ContainsKey("$within"));
        var innerWithin = results["$within"] as IDictionary<string, object>;
        Assert.IsTrue(innerWithin.ContainsKey("$box"));
        Assert.AreEqual(2, (innerWithin["$box"] as IEnumerable).Cast<object>().Count());


    }

    [TestMethod]
    [Description("Tests that WhereFullTextMatches correctly builds the nested $text and $search query.")]
    public void WhereFullTextMatches_BuildsQueryCorrectly()
    {
        // Arrange & Act: Search for the term "dotnet" inside the "bio" field
        var query = new ParseQuery<ParseObject>(MockHub.Object, "Profile")
            .WhereFullTextMatches("bio", "dotnet");

        var results = query.GetConstraint("bio") as IDictionary<string, object>;

        // Assert: Verify the MongoDB nested structure
        Assert.IsNotNull(results, "The constraint for 'bio' should not be null.");
        Assert.IsTrue(results.ContainsKey("$text"), "The constraint must contain the '$text' operator.");

        var textDict = results["$text"] as IDictionary<string, object>;
        Assert.IsNotNull(textDict);
        Assert.IsTrue(textDict.ContainsKey("$search"), "The '$text' block must contain the '$search' operator.");

        var searchDict = textDict["$search"] as IDictionary<string, object>;
        Assert.IsNotNull(searchDict);
        Assert.IsTrue(searchDict.ContainsKey("$term"), "The '$search' block must contain the '$term' key.");
        Assert.AreEqual("dotnet", searchDict["$term"], "The search term must match the input query.");
    }

    [ParseClassName("ComplexObject")]
    public class ComplexObject : ParseObject
    {
        [ParseFieldName("user_profile")]
        public ComplexObject Profile { get; set; }

        [ParseFieldName("views_count")]
        public int Views { get; set; }

        [ParseFieldName("is_active")]
        public bool IsActive { get; set; }
    }

    [TestMethod]
    [Description("Tests that ExpressionHelper correctly extracts single and deep nested properties with their [ParseFieldName] attributes.")]
    public void ExpressionHelper_ResolvesDeepPropertiesCorrectly()
    {
        // 1. Simple Property with attribute
        var field = ExpressionHelper.GetParseFieldName<ComplexObject, int>(x => x.Views);
        Assert.AreEqual("views_count", field);

        // 2. Boolean Property (Verifies Unary/Convert expressions are unwrapped)
        var activeField = ExpressionHelper.GetParseFieldName<ComplexObject, bool>(x => x.IsActive);
        Assert.AreEqual("is_active", activeField);

        // 3. Deep Nested Property pathing
        var deepField = ExpressionHelper.GetParseFieldName<ComplexObject, int>(x => x.Profile.Profile.Views);
        Assert.AreEqual("user_profile.user_profile.views_count", deepField, "The path should recursively append nested members.");
    }

    [TestMethod]
    [Description("Tests that strongly typed query overloads successfully append constraints.")]
    public void Query_WithStronglyTypedExpression_AppendsConstraint()
    {
        var query = new ParseQuery<ComplexObject>(MockHub.Object,"ComplexObject")
            .WhereEqualTo(x => x.Views, 42)
            .Include(x => x.Profile.Profile);
        // Assert EqualTo
        var constraint = query.GetConstraint("views_count");
        Assert.AreEqual(42, constraint);

        // Assert Include path
        var queryParams = query.BuildParameters();
        Assert.AreEqual("user_profile.user_profile", queryParams["include"]);
    }
}