namespace DSMarketService.Infrastructure.Redis;

/// <summary>
/// Xác định Redis vật lý cần đọc. KHÔNG dựa vào số DB để chọn Redis:
/// DB0 ở Redis Sentinel khác hoàn toàn DB0 ở Redis Legacy.
/// </summary>
public enum RedisSource
{
    Sentinel,
    Legacy
}

/// <summary>
/// Sở hữu hai kết nối Redis độc lập, mỗi kết nối được tạo một lần cho mỗi Pod.
/// Nghiệp vụ phải truyền RedisSource rõ ràng, không dùng singleton Redis chung cho mọi API.
/// </summary>
public sealed class RedisConnections : IAsyncDisposable
{
    private ConnectionMultiplexer? _redisSentinel;
    private ConnectionMultiplexer? _redisLegacy;

    /// <summary>Chỉ Ready khi cả hai Redis phục vụ các API hiện có đều đã kết nối.</summary>
    public bool IsReady => _redisSentinel?.IsConnected == true && _redisLegacy?.IsConnected == true;

    /// <summary>
    /// Khởi tạo async lúc Pod khởi động, không mở Redis connection trên từng HTTP request.
    /// ServiceName chỉ được điền khi RedisSentinel chứa địa chỉ các Sentinel node (thường port 26379).
    /// </summary>
    public async Task InitializeAsync(IConfiguration config)
    {
        if (_redisSentinel is not null || _redisLegacy is not null)
            throw new InvalidOperationException("RedisConnections đã được khởi tạo.");

        var sentinelOptions = CreateOptions(config["Redis:RedisSentinel"], "Redis:RedisSentinel");
        var serviceName = config["Redis:SentinelServiceName"];
        if (!string.IsNullOrWhiteSpace(serviceName))
            sentinelOptions.ServiceName = serviceName;

        var legacyOptions = CreateOptions(config["Redis:RedisLegacy"], "Redis:RedisLegacy");
        // RedisLegacy là Redis standalone: không được có serviceName/Sentinel mode.
        if (!string.IsNullOrWhiteSpace(legacyOptions.ServiceName))
            throw new InvalidOperationException("Redis:RedisLegacy không được khai báo serviceName.");

        ConnectionMultiplexer? sentinel = null;
        try
        {
            sentinel = await ConnectionMultiplexer.ConnectAsync(sentinelOptions).ConfigureAwait(false);
            var legacy = await ConnectionMultiplexer.ConnectAsync(legacyOptions).ConfigureAwait(false);
            _redisSentinel = sentinel;
            _redisLegacy = legacy;
        }
        catch
        {
            sentinel?.Dispose();
            throw;
        }
    }

    private static ConfigurationOptions CreateOptions(string? connectionString, string name)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException($"Thiếu cấu hình {name}. Hai Redis bắt buộc khai báo riêng.");
        var options = ConfigurationOptions.Parse(connectionString);
        if (options.EndPoints.Count == 0)
            throw new InvalidOperationException($"{name} không có Redis endpoint hợp lệ.");
        options.AbortOnConnectFail = false;
        options.KeepAlive = 30;
        return options;
    }

    public IDatabase GetDatabase(RedisSource source, int db)
    {
        // Không tự động fallback sang Redis còn lại khi mất kết nối:
        // fallback có thể trả nhầm dữ liệu giá hoặc dữ liệu cũ cho web/mobile.
        var multiplexer = source switch
        {
            RedisSource.Sentinel => _redisSentinel,
            RedisSource.Legacy => _redisLegacy,
            _ => throw new ArgumentOutOfRangeException(nameof(source))
        };
        if (multiplexer is null)
            throw new InvalidOperationException($"Redis {source} chưa khởi tạo.");
        return multiplexer.GetDatabase(db);
    }

    public async ValueTask DisposeAsync()
    {
        if (_redisSentinel is not null)
        {
            try { await _redisSentinel.CloseAsync(); }
            finally { _redisSentinel.Dispose(); }
        }
        if (_redisLegacy is not null)
        {
            try { await _redisLegacy.CloseAsync(); }
            finally { _redisLegacy.Dispose(); }
        }
    }
}
