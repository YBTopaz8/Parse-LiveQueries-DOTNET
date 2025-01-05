namespace Parse.Abstractions.Infrastructure;

/// <summary>
/// A unit that can generate a relative path to a persistent storage file.
/// </summary>
public interface IRelativeCacheLocationGenerator
{
    /// <summary>
    /// The corresponding relative path generated by this <see cref="IRelativeCacheLocationGenerator"/>.
    /// </summary>
    string GetRelativeCacheFilePath(IServiceHub serviceHub);
}
