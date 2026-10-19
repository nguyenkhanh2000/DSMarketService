namespace DSMarketService.Controllers;

/// <summary>
/// Các API giá, lịch sử khớp lệnh, dữ liệu thỏa thuận và biểu đồ.
/// Mỗi action riêng biệt: không switch theo chuỗi và không tạo thread trên request.
/// </summary>
[Route("ds_market")]
public sealed class MarketPricesController(MarketReadService read, MarketKeyResolver keys,
    HistorySeriesService history) : MarketControllerBase
{
    [HttpGet("price_ladder")]
    public async Task<IActionResult> PriceLadder()
    {
        var from = QDouble("from", 0);
        var to   = QDouble("to", -1);
        return LegacyJson(await read.ReadSeriesAsync(RedisSource.Sentinel, keys.Db1,
            "PRICE_LADDER:" + Q("Code").Trim().ToUpperInvariant(),
            from, to, true, from == 0 && to == -1, ClientToken, floatingScore: true));
    }

    [HttpGet("pt")]
    public async Task<IActionResult> PutThrough()
    {
        var from = QLong("from", 0);
        var to   = QLong("to", -1);
        return LegacyJson(await read.ReadSeriesAsync(RedisSource.Legacy, keys.Db0, Q("keyName").Trim().ToUpperInvariant(),
            from, to, true, from == 0 && to == -1, ClientToken));
    }

    [HttpGet("matching")]
    public async Task<IActionResult> Matching() => LegacyJson(await read.ReadSeries1Async(
        RedisSource.Sentinel,         "LS:" + Q("stockCode").ToUpperInvariant(), QLong("from", 0), QLong("to", -1), true, ClientToken));

    [HttpGet("history")]
    public async Task<IActionResult> History() => LegacyJson(await history.ReadAsync(
        Q("symbol"), Q("from"), Q("to"), Q("resolution", "1"), QInt("countback", 0), ClientToken));

    [HttpGet("last_index")]
    public async Task<IActionResult> LastIndex() => LegacyJson(await read.LastIndexAsync(Exchange, ClientToken));
}
