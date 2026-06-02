using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NovaTechCRM.Services.Interfaces;
using StackExchange.Redis;

namespace NovaTechCRM.Infrastructure.Cache;

// Redis-backed ICacheService — drop-in replacement for the in-memory CacheService.
// Register this in production; CacheService (in-memory) is fine for single-instance dev.
// See NOVA-50 for the history of why this wasn't prioritised sooner.
public class RedisCacheService : ICacheService
{
    private readonly IDatabase _db;
    private readonly ILogger<RedisCacheService> _logger;

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public RedisCacheService(
        IConnectionMultiplexer redis,
        ILogger<RedisCacheService> logger)
    {
        _db     = redis.GetDatabase();
        _logger = logger;
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
        where T : class
    {
        var raw = await _db.StringGetAsync(key);
        if (raw.IsNullOrEmpty) return null;

        try
        {
            return JsonSerializer.Deserialize<T>(raw!, _jsonOpts);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis deserialization failed for key {Key}", key);
            return null;
        }
    }

    public async Task SetAsync<T>(
        string key, T value, TimeSpan? expiry = null, CancellationToken ct = default)
        where T : class
    {
        var json = JsonSerializer.Serialize(value, _jsonOpts);
        await _db.StringSetAsync(key, json, expiry ?? TimeSpan.FromMinutes(15));
    }

    public async Task RemoveAsync(string key, CancellationToken ct = default)
        => await _db.KeyDeleteAsync(key);

    public async Task RemoveByPrefixAsync(string prefix, CancellationToken ct = default)
    {
        // SCAN is safer than KEYS for production — non-blocking on large keyspaces
        var server  = _db.Multiplexer.GetServer(_db.Multiplexer.GetEndPoints().First());
        var keys    = server.KeysAsync(pattern: $"{prefix}*");
        var deleted = 0;

        await foreach (var key in keys)
        {
            await _db.KeyDeleteAsync(key);
            deleted++;
        }

        _logger.LogDebug("Redis: removed {Count} keys with prefix '{Prefix}'", deleted, prefix);
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken ct = default)
        => await _db.KeyExistsAsync(key);

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

public class RedisOptions
{
    public string ConnectionString { get; set; } = "localhost:6379";
    public string InstanceName     { get; set; } = "novatech:";
}
