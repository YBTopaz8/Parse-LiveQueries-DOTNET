using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Parse.Abstractions.Platform.Objects;

namespace Parse.Abstractions.Platform.Queries;

public interface IParseQueryController
{
    Task<IEnumerable<IObjectState>> FindAsync<T>(ParseQuery<T> query, ParseUser user, CancellationToken cancellationToken = default) where T : ParseObject;

    Task<int> CountAsync<T>(ParseQuery<T> query, ParseUser user, CancellationToken cancellationToken = default) where T : ParseObject;

    Task<IObjectState> FirstAsync<T>(ParseQuery<T> query, ParseUser user, CancellationToken cancellationToken = default) where T : ParseObject;


    Task<IEnumerable<IDictionary<string, object>>?> AggregateAsync<T>(ParseQuery<T> query, IList<object> pipeline, ParseUser user, CancellationToken cancellationToken = default) where T : ParseObject;
    Task<IEnumerable<TResult>?> DistinctAsync<T, TResult>(ParseQuery<T> query, string key, ParseUser user, CancellationToken cancellationToken = default) where T : ParseObject;
}
