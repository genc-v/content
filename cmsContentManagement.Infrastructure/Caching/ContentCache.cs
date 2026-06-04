using System.Text.Json;
using System.Text.Json.Serialization;
using cmsContentManagement.Application.Common.Settings;
using cmsContentManagement.Application.Interfaces;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace cmsContentManagment.Infrastructure.Caching;

/// <summary>
/// Redis-backed read-through cache keyed by a caller-supplied key. Each entry is
/// invalidated explicitly via <see cref="RemoveAsync"/> when its underlying record
/// changes. Redis is treated as best-effort — any failure falls back to the source
/// so a cache outage can never take down a request.
/// </summary>
public class ContentCache : IContentCache
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        // Domain entities have navigation collections that point back to one another.
        ReferenceHandler = ReferenceHandler.IgnoreCycles
    };

    private readonly IDistributedCache _cache;
    private readonly ILogger<ContentCache> _logger;
    private readonly TimeSpan _ttl;

    public ContentCache(IDistributedCache cache, IOptions<CacheSettings> options, ILogger<ContentCache> logger)
    {
        _cache = cache;
        _logger = logger;
        var seconds = options.Value.ContentTtlSeconds;
        _ttl = TimeSpan.FromSeconds(seconds > 0 ? seconds : 60);
    }

    public async Task<T?> GetOrCreateAsync<T>(string key, Func<Task<T?>> factory) where T : class
    {
        try
        {
            var cached = await _cache.GetStringAsync(key);
            if (cached != null)
            {
                var hit = JsonSerializer.Deserialize<T>(cached, SerializerOptions);
                if (hit != null) return hit;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis read failed for {Key}; serving from source", key);
            return await factory();
        }

        var result = await factory();
        if (result == null) return null;

        try
        {
            var payload = JsonSerializer.Serialize(result, SerializerOptions);
            await _cache.SetStringAsync(key, payload, new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = _ttl
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis write failed for {Key}", key);
        }

        return result;
    }

    public async Task RemoveAsync(string key)
    {
        try
        {
            await _cache.RemoveAsync(key);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis remove failed for {Key}", key);
        }
    }
}
