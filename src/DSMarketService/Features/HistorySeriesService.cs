namespace DSMarketService.Features;

/// <summary>
/// Engine nến OHLCV mới: không tạo Thread, không dùng Dictionary, xử lý một lần qua dữ liệu.
/// Tách quy tắc gộp nến để có thể đối chiếu với bộ dữ liệu lịch sử chuẩn.
/// </summary>
public sealed class HistorySeriesService(RedisReader redis, MarketKeyResolver keys, IConfiguration cfg)
{
    // Chặn số tác vụ CPU/chart đồng thời, KHÔNG phải giới hạn số request HTTP.
    private static readonly SemaphoreSlim ChartWork = new(12, 12);
    private readonly TimeZoneInfo _zone = TimeZoneInfo.FindSystemTimeZoneById(cfg["Market:TimeZoneId"] ?? "Asia/Ho_Chi_Minh");

    public async Task<ApiEnvelope> ReadAsync(string symbols, string from, string to, string resolution,
        int countback, CancellationToken ct)
    {
        var names = symbols.Split(',', StringSplitOptions.None).Take(30).ToArray();
        var numeric = int.TryParse(resolution, out var interval) && interval > 0;
        long start = long.Parse(from + "00000", CultureInfo.InvariantCulture);
        long end = long.Parse(to + "00000", CultureInfo.InvariantCulture);
        var results = new CandleSeries[names.Length];
        for (int offset = 0; offset < names.Length; offset += 4)
        {
            var localStart = offset;
            var tasks = Enumerable.Range(localStart, Math.Min(4, names.Length - localStart))
                .Select(async i =>
                {
                    await ChartWork.WaitAsync(ct);
                    try { results[i] = await ReadSymbolAsync(names[i], start, end, numeric, interval, countback, ct); }
                    finally { ChartWork.Release(); }
                });
            await Task.WhenAll(tasks);
        }
        return ApiEnvelope.Ok(results);
    }

    private async Task<CandleSeries> ReadSymbolAsync(string original, long start, long end,
        bool intraday, int interval, int countback, CancellationToken ct)
    {
        var symbol = original == "UPCOMINDEX" ? "HNXUPCOMINDEX" : original;
        var key = (intraday ? "INTRADAY:S6G__" : "HISTORY:S6G__") + symbol;
        // Redis điểm số theo YYYYMMDDHHMMSS...; cần so sánh chính xác với dữ liệu producer.
        var entries = await redis.SortedAsync(RedisSource.Sentinel, keys.Db3, key, start, end, false, false, ct);
        var required = intraday ? (long)countback * interval : countback;
        var insufficient = entries.LongLength < required;
        if (insufficient) entries = await redis.SortedAsync(RedisSource.Sentinel, keys.Db3, key, 0, end, false, false, ct);
        var candles = new List<Candle>(entries.Length);
        // Parse đúng một lần; thời gian legacy lưu trong Data.t hoặc Time riêng.
        foreach (var entry in entries)
        {
            if (JToken.Parse(entry.Element.ToString()) is not JObject root || root["Data"] is not JObject d) continue;
            var stamp = (string?)d["t"] ?? "";
            var time = ToUnix(stamp, (string?)root["Time"]);
            candles.Add(new Candle(time, (double?)d["o"] ?? 0, (double?)d["h"] ?? 0,
                (double?)d["l"] ?? 0, (double?)d["c"] ?? 0, (long?)d["v"] ?? 0));
        }

        var series = new CandleSeries { Symbol = symbol };
        if (!intraday || interval == 1)
        {
            // Legacy đi từ nến mới -> cũ, sau đó Reverse trước khi trả.
            for (var i = candles.Count - 1; i >= 0; i--)
            {
                Append(series, candles[i]);
                if (insufficient && countback > 0 && series.t.Count >= countback) break;
            }
        }
        else
        {
            // Gộp nến theo nhóm thời gian; giữ Open của bản ghi cũ nhất, Close của mới nhất.
            var group = new List<Candle>();
            long bucket = long.MinValue;
            for (int i = candles.Count - 1; i >= 0; i--)
            {
                var candle = candles[i];
                var slot = Bucket(candle.Time, interval);
                if (bucket != long.MinValue && slot != bucket && group.Count > 0)
                {
                    Append(series, Aggregate(group));
                    if (insufficient && countback > 0 && series.t.Count >= countback) break;
                    group.Clear();
                }
                bucket = slot;
                group.Add(candle);
            }
            if (group.Count > 0 && (!insufficient || countback == 0 || series.t.Count < countback))
                Append(series, Aggregate(group));
        }
        series.t.Reverse(); series.o.Reverse(); series.h.Reverse(); series.l.Reverse();
        series.c.Reverse(); series.v.Reverse();
        return series;
    }

    private static void Append(CandleSeries s, Candle c)
    {
        s.t.Add(c.Time); s.o.Add(c.Open); s.h.Add(c.High); s.l.Add(c.Low); s.c.Add(c.Close); s.v.Add(c.Volume);
    }
    private static Candle Aggregate(List<Candle> group)
    {
        var oldest = group[^1]; var newest = group[0];
        var high = group.Max(x => x.High); var low = group.Min(x => x.Low);
        long volume = 0;
        foreach (var c in group) volume += c.Volume;
        return new Candle(oldest.Time, oldest.Open, high, low, newest.Close, volume);
    }
    private static long Bucket(long epoch, int minutes)
    {
        var seconds = (long)minutes * 60;
        return epoch - (epoch % seconds);
    }
    private long ToUnix(string input, string? producerTime)
    {
        if (long.TryParse(input, CultureInfo.InvariantCulture, out var unix)) return unix;
        var day = producerTime?.Split(' ')[0];
        var raw = input.Contains('-') ? input : (day is null ? input : day + " " + input);
        if (!DateTime.TryParseExact(raw, new[] { "yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss" },
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)) return 0;
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(time, DateTimeKind.Unspecified), _zone))
            .ToUnixTimeSeconds();
    }
    private sealed record Candle(long Time, double Open, double High, double Low, double Close, long Volume);
}
