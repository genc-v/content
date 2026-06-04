namespace cmsContentManagement.Application.Interfaces;

public interface IContentCache
{
    /// <summary>
    /// Returns the cached value for <paramref name="key"/>, or runs <paramref name="factory"/>
    /// and caches its result. A <c>null</c> result is never cached.
    /// </summary>
    Task<T?> GetOrCreateAsync<T>(string key, Func<Task<T?>> factory) where T : class;

    /// <summary>
    /// Removes the cached value for <paramref name="key"/> so the next read is served fresh.
    /// </summary>
    Task RemoveAsync(string key);
}
