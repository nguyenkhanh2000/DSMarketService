namespace DSMarketService.Infrastructure.Redis;

/// <summary>
/// Đọc Redis qua 2 Multiplexer tách biệt (Sentinel / Legacy) trên mỗi Pod;
/// không mở kết nối/lock/Task.Run trên mỗi HTTP request.
/// </summary>
public sealed class RedisReader(RedisConnections connections)
{
    public async Task<string?> StringAsync(RedisSource source, int db, string key, CancellationToken ct)
    {
        var raw = await connections.GetDatabase(source, db).StringGetAsync(key, RedisCommandFlags.DemandMaster).WaitAsync(ct);
        return raw.IsNull ? null : raw.ToString();
    }

    public async Task<HashEntry[]> HashAsync(RedisSource source, int db, string key, CancellationToken ct) =>
        await connections.GetDatabase(source, db).HashGetAllAsync(key, RedisCommandFlags.DemandMaster).WaitAsync(ct);

    public async Task<SortedSetEntry[]> SortedAsync(RedisSource source, int db, string key, double from, double to,
        bool descending, bool byRank, CancellationToken ct)
    {
        var redis = connections.GetDatabase(source, db);
        var order = descending ? Order.Descending : Order.Ascending;
        if (!byRank && to == -1 && from != 0) to = double.PositiveInfinity;
        return byRank
            ? await redis.SortedSetRangeByRankWithScoresAsync(key, 0, -1, order, RedisCommandFlags.DemandMaster).WaitAsync(ct)
            : await redis.SortedSetRangeByScoreWithScoresAsync(key, from, to, Exclude.None, order,
                0, -1, RedisCommandFlags.DemandMaster).WaitAsync(ct);
    }

    // Một số key thực tế lưu JSON dạng string chứa object JSON => gỡ tối đa một lớp.
    public static JToken? Decode(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        var token = JToken.Parse(raw);
        if (token.Type == JTokenType.String && !string.IsNullOrWhiteSpace(token.Value<string>()))
            token = JToken.Parse(token.Value<string>()!);
        return token;
    }

    public static JArray WithScore(IEnumerable<SortedSetEntry> rows, bool floatingScore = false)
    {
        var output = new JArray();
        foreach (var row in rows)
        {
            var item = JObject.Parse(row.Element.ToString());
            item["SCORE"] = floatingScore ? new JValue(row.Score) : new JValue((long)row.Score);
            output.Add(item);
        }
        return output;
    }
}
