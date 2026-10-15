namespace DSMarketService.Controllers;

/// <summary>Các bảng xếp hạng và giao dịch nhà đầu tư nước ngoài.</summary>
[Route("ds_market")]
public sealed class MarketRankingsController(MarketReadService read) : MarketControllerBase
{
    [HttpGet("top_cp")]
    public async Task<IActionResult> TopCompanies()
    {
        var type = Q("type");
        var month = Q("monthNumber");
        var fill = Q("fill");
        var key = month != ""
            ? (type.ToUpperInvariant() == "VOLATILITY"
                ? $"TOP_CP:{type}:{fill}:{Exchange.ToUpperInvariant()}_{month}"
                : $"TOP_CP:{type}:{Exchange.ToUpperInvariant()}_{month}")
            : $"TOP_CP:{type}:{Exchange.ToUpperInvariant()}_{(fill == "" ? "5" : fill)}";
        return LegacyJson(await read.ReadString1Async(RedisSource.Sentinel, key, true, ClientToken));
    }

    [HttpGet("top_value")]
    public async Task<IActionResult> TopValue() => LegacyJson(await read.ReadString1Async(RedisSource.Sentinel, 
        $"TOP_CP:VAL:{Exchange.ToUpperInvariant()}_{QInt("dayNumber", 0)}", true, ClientToken));

    [HttpGet("top_volume")]
    public async Task<IActionResult> TopVolume() => LegacyJson(await read.ReadString1Async(RedisSource.Sentinel, 
        $"TOP_CP:VOL:{Exchange.ToUpperInvariant()}_{QInt("dayNumber", 0)}", true, ClientToken));

    [HttpGet("trade_nn/val")]
    public async Task<IActionResult> ForeignValue() => LegacyJson(await read.ReadSeries1Async(RedisSource.Sentinel, 
        $"GDNN:VAL:{Exchange.ToUpperInvariant()}:{QInt("dayNumber", 0)}",
        QLong("from", 0), QLong("to", -1), false, ClientToken));

    [HttpGet("trade_nn/vol")]
    public async Task<IActionResult> ForeignVolume() => LegacyJson(await read.ReadSeries1Async(RedisSource.Sentinel, 
        $"GDNN:VOL:{Exchange.ToUpperInvariant()}:{QInt("dayNumber", 0)}",
        QLong("from", 0), QLong("to", -1), false, ClientToken));
}
