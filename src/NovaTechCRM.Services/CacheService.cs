using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using NovaTechCRM.Services.Interfaces;

namespace NovaTechCRM.Services;

// In-memory cache implementation — used in development and single-instance prod deployments.
// For multi-instance: swap for RedisCacheService (in Infrastructure layer).
// TODO: we kept meaning to switch to Redis but it never got prioritised (NOVA-50)
public class CacheService : ICacheService
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<CacheService> _logger;

    // track keys for prefix-based eviction — IMemoryCache doesn't support this natively
    private static readonly HashSet<string> _trackedKeys = new();
    private static readonly object _keysLock = new();

    private static readonly TimeSpan _defaultExpiry = TimeSpan.FromMinutes(15);

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public CacheService(IMemoryCache cache, ILogger<CacheService> logger)
    {
        _cache  = cache;
        _logger = logger;
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default) where T : class
    {
        await Task.CompletedTask;  // IMemoryCache is sync — async interface for Redis compat

        if (_cache.TryGetValue(key, out var raw) && raw is string json)
        {
            try
            {
                return JsonSerializer.Deserialize<T>(json, _jsonOpts);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Cache deserialization failed for key {Key}", key);
                return null;
            }
        }

        return null;
    }

    public async Task SetAsync<T>(
        string key, T value, TimeSpan? expiry = null, CancellationToken ct = default)
        where T : class
    {
        await Task.CompletedTask;

        var json = JsonSerializer.Serialize(value, _jsonOpts);
        var options = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = expiry ?? _defaultExpiry
        };

        _cache.Set(key, json, options);

        lock (_keysLock)
            _trackedKeys.Add(key);
    }

    public async Task RemoveAsync(string key, CancellationToken ct = default)
    {
        await Task.CompletedTask;
        _cache.Remove(key);

        lock (_keysLock)
            _trackedKeys.Remove(key);
    }

    public async Task RemoveByPrefixAsync(string prefix, CancellationToken ct = default)
    {
        await Task.CompletedTask;

        List<string> toRemove;
        lock (_keysLock)
        {
            toRemove = _trackedKeys.Where(k => k.StartsWith(prefix)).ToList();
        }

        foreach (var key in toRemove)
        {
            _cache.Remove(key);
            lock (_keysLock)
                _trackedKeys.Remove(key);
        }

        _logger.LogDebug("Removed {Count} cache entries with prefix '{Prefix}'",
            toRemove.Count, prefix);
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken ct = default)
    {
        await Task.CompletedTask;
        return _cache.TryGetValue(key, out _);
    }

    public async Task<T> GetOrSetAsync<T>(
        string key,
        Func<Task<T>> factory,
        TimeSpan? expiry = null,
        CancellationToken ct = default) where T : class
    {
        var cached = await GetAsync<T>(key, ct);
        if (cached != null) return cached;

        var value = await factory();
        await SetAsync(key, value, expiry, ct);
        return value;
    }
}
