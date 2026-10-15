namespace DSMarketService.Features;

/// <summary>Đường nóng của API: phần lớn chỉ Redis async -> token JSON -> response, không DI xuyên nhiều lớp.</summary>
public sealed class MarketReadService(RedisReader redis, MarketKeyResolver keys)
{
    public async Task<ApiEnvelope> ReadStringAsync(RedisSource source, int db, string key, bool emptyOnMissing, CancellationToken ct)
    {
        var value = RedisReader.Decode(await redis.StringAsync(source, db, key, ct));
        return ApiEnvelope.Ok(value ?? (emptyOnMissing ? (object)"" : null));
    }

    public async Task<ApiEnvelope> ReadSeriesAsync(RedisSource source, int db, string key, double from, double to,
        bool descending, bool byRank, CancellationToken ct, bool includeScore = true, bool floatingScore = false)
    {
        var entries = await redis.SortedAsync(source, db, key, from, to, descending, byRank, ct);
        if (includeScore) return ApiEnvelope.Ok(RedisReader.WithScore(entries, floatingScore));
        var array = new JArray();
        // GDNN không có SCORE và đảo lại thứ tự khi trả về.
        foreach (var item in entries.Reverse()) array.Add(JToken.Parse(item.Element.ToString()));
        return ApiEnvelope.Ok(array);
    }

    public Task<ApiEnvelope> ReadString0Async(RedisSource source, string key, bool empty, CancellationToken ct) =>
        ReadStringAsync(source, keys.Db0, key, empty, ct);
    public Task<ApiEnvelope> ReadString1Async(RedisSource source, string key, bool empty, CancellationToken ct) =>
        ReadStringAsync(source, keys.Db1, key, empty, ct);
    public Task<ApiEnvelope> ReadSeries1Async(RedisSource source, string key, double from, double to, bool descending,
        CancellationToken ct) => ReadSeriesAsync(source, keys.Db1, key, from, to, descending, from == 0 && to == -1, ct);

    public async Task<ApiEnvelope> PortfolioAsync(string exchange, CancellationToken ct)
    {
        var data = RedisReader.Decode(await redis.StringAsync(RedisSource.Legacy, keys.Db0, keys.Portfolio(exchange), ct));
        var content = (data as JObject)?["Data"];
        if (exchange == "hnxupcomindex") content = content is JArray arr && arr.Count > 0 ? arr[0]?["HNXUpcomIndex"] : null;
        return ApiEnvelope.Ok(content);
    }

    public async Task<ApiEnvelope> WorldIndicesAsync(CancellationToken ct)
    {
        var data = await redis.HashAsync(RedisSource.Sentinel, keys.Db3, keys.Key("key_LATEST_INVESTING"), ct);
        var map = new Dictionary<string, string>
        {
            ["Dow_Jones"]="Dow Jones",["FTSE_100"]="FTSE 100",["CAC_40"]="CAC 40",
            ["Nikkei_225"]="Nikkei 225",["SP_ASX_200"]="S&P/ASX 200"
        };
        var output = new JArray();
        foreach (var row in data)
        {
            if (RedisReader.Decode(row.Value.ToString()) is not JObject item) continue;
            var symbol = (string?)item["Symbol"];
            if (symbol is not null && map.TryGetValue(symbol, out var mapped)) item["Symbol"] = mapped;
            // Chuyển sang DTO chuỗi để giữ schema 10 trường của API cũ.
            output.Add(JToken.FromObject(item.ToObject<WorldIndexQuote>()!));
        }
        return ApiEnvelope.Ok(output);
    }
    public async Task<ApiEnvelope> LastIndexAsync(string exchange, CancellationToken ct)
    {
        var key = "S5G_LAST_INDEX_" + exchange.Trim().ToUpperInvariant();
        var raw = await redis.StringAsync(RedisSource.Legacy, keys.Db0, key, ct);
        var token = RedisReader.Decode(raw);
        return ToLastIndexResponse(token);
    }
    /// <summary>
    /// Pure mapping để kiểm thử response mà không cần kết nối Redis/Kafka.
    /// Null khi key chưa có dữ liệu; không tự giả lập số liệu ngày mới.
    /// </summary>
    public static ApiEnvelope ToLastIndexResponse(JToken? stored)
    {
        JToken? data = stored switch
        {
            JObject obj => obj["Data"],
            JArray array => array,
            null => null,
            _ => throw new JsonSerializationException("last_index: unexpected Redis JSON shape")
        };

        // Không trả nhầm object {Time, Data} dưới Data và cũng không báo success sai schema.
        if (data is not null && data is not JArray)
            throw new JsonSerializationException("last_index: Redis field Data must be a JSON array");

        return ApiEnvelope.Ok(data);
    }
    /// <summary>
    /// test ko trả theo ApiEnvelope
    /// </summary>
    /// <param name="exchange"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    public async Task<string?> LastIndexRawAsync(string exchange, CancellationToken ct)
    {
        var data = RedisReader.Decode(await redis.StringAsync(RedisSource.Legacy, keys.Db0, "S5G_LAST_INDEX_" + exchange.ToUpperInvariant(), ct));
        return data?.ToString(Newtonsoft.Json.Formatting.None);
    }
    
}
