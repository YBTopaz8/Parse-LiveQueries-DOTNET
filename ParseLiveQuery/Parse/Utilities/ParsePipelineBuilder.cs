#nullable enable
using Parse;
using Parse.Infrastructure.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace YB.Parse.LiveQuery.Parse.Utilities;

public enum ParseAccumulator
{
    Sum, Avg, Min, Max, First, Last, Push, AddToSet
}

/// <summary>
/// A fluent builder for constructing MongoDB/Parse Server aggregation pipelines.
/// </summary>
public class ParsePipelineBuilder<T> where T : ParseObject
{
    private readonly ParseQuery<T> _sourceQuery;
    private readonly List<object> _pipeline = new();

    public ParsePipelineBuilder(ParseQuery<T> sourceQuery)
    {
        _sourceQuery = sourceQuery ?? throw new ArgumentNullException(nameof(sourceQuery));
    }

    /// <summary>
    /// Filters the documents to pass only the documents that match the specified ParseQuery conditions.
    /// </summary>
    public ParsePipelineBuilder<T> Match(ParseQuery<T> query)
    {
        if (query == null) throw new ArgumentNullException(nameof(query));

        if (query.BuildParameters().TryGetValue("where", out var filters))
        {
            _pipeline.Add(new Dictionary<string, object> { { "$match", filters } });
        }
        return this;
    }

    #region Group

    /// <summary>
    /// Groups input documents by the specified field and applies accumulators (like Sum, Avg).
    /// </summary>
    /// <param name="groupByField">The field to group by. (Pass null to group all documents into one).</param>
    /// <param name="accumulators">A tuple defining the output name, the operator, and the target field.</param>
    public ParsePipelineBuilder<T> Group(string? groupByField, params (string OutputField, ParseAccumulator Accumulator, string TargetField)[] accumulators)
    {
        object? mongoGroupId = groupByField == null ? null : $"${groupByField}";
        var groupStage = new Dictionary<string, object?> { { "_id", mongoGroupId } };

        if (accumulators != null)
        {
            foreach (var (OutputField, Accumulator, TargetField) in accumulators)
            {
                string mongoOperator = Accumulator switch
                {
                    ParseAccumulator.Sum => "$sum",
                    ParseAccumulator.Avg => "$avg",
                    ParseAccumulator.Min => "$min",
                    ParseAccumulator.Max => "$max",
                    ParseAccumulator.First => "$first",
                    ParseAccumulator.Last => "$last",
                    ParseAccumulator.Push => "$push",
                    ParseAccumulator.AddToSet => "$addToSet",
                    _ => "$sum"
                };

                // If target field is a pure number (like Count = Sum: 1), use it directly. Otherwise, prefix with $.
                object mongoTarget = int.TryParse(TargetField, out int val) ? val : $"${TargetField}";
                groupStage[OutputField] = new Dictionary<string, object> { { mongoOperator, mongoTarget } };
            }
        }

        _pipeline.Add(new Dictionary<string, object> { { "$group", groupStage } });
        return this;
    }

    /// <summary>
    /// Strongly-typed Grouping using a property expression.
    /// </summary>
    public ParsePipelineBuilder<T> Group<TProp>(Expression<Func<T, TProp>>? groupByFieldSelector, params (string OutputField, ParseAccumulator Accumulator, string TargetField)[] accumulators)
    {
        
        string? fieldName = groupByFieldSelector == null ? null : ExpressionHelper.GetParseFieldName(groupByFieldSelector);
        return Group(fieldName, accumulators);
    }

    #endregion

    #region Project

    public ParsePipelineBuilder<T> Project(params string[] fieldsToInclude)
    {
        if (fieldsToInclude == null || fieldsToInclude.Length == 0) return this;

        var projection = new Dictionary<string, object>();
        foreach (var field in fieldsToInclude)
        {
            projection[field] = 1;
        }

        _pipeline.Add(new Dictionary<string, object> { { "$project", projection } });
        return this;
    }

    /// <summary>
    /// Strongly-typed Projection using property expressions.
    /// </summary>
    public ParsePipelineBuilder<T> Project(params Expression<Func<T, object>>[] fieldSelectors)
    {
        if (fieldSelectors == null || fieldSelectors.Length == 0) return this;

        var fields = fieldSelectors.Select(ExpressionHelper.GetParseFieldName).ToArray();
        return Project(fields);
    }

    #endregion

    public ParsePipelineBuilder<T> Limit(int count)
    {
        _pipeline.Add(new Dictionary<string, object> { { "$limit", count } });
        return this;
    }

    public ParsePipelineBuilder<T> Skip(int count)
    {
        _pipeline.Add(new Dictionary<string, object> { { "$skip", count } });
        return this;
    }

    #region Sort

    public ParsePipelineBuilder<T> Sort(string fieldName, bool descending = false)
    {
        if (string.IsNullOrEmpty(fieldName)) throw new ArgumentNullException(nameof(fieldName));

        _pipeline.Add(new Dictionary<string, object>
        {
            { "$sort", new Dictionary<string, object> { { fieldName, descending ? -1 : 1 } } }
        });
        return this;
    }

    /// <summary>
    /// Strongly-typed Sort using a property expression.
    /// </summary>
    public ParsePipelineBuilder<T> Sort<TProp>(Expression<Func<T, TProp>> fieldSelector, bool descending = false)
    {
        return Sort(ExpressionHelper.GetParseFieldName(fieldSelector), descending);
    }

    #endregion

    /// <summary>
    /// Executes the built aggregation pipeline against the Parse Server.
    /// </summary>
    public async Task<IEnumerable<IDictionary<string, object>>?> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var user = await _sourceQuery.Services.GetCurrentUser().ConfigureAwait(false);
        return await _sourceQuery.Services.QueryController.AggregateAsync(_sourceQuery, _pipeline, user, cancellationToken).ConfigureAwait(false);
    }
}