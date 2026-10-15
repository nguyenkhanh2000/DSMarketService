namespace DSMarketService.Controllers;

/// <summary>Các API dữ liệu ngày giao dịch, lịch nghỉ và chỉ số thế giới.</summary>
[Route("ds_market")]
public sealed class MarketCalendarController(
    MarketReadService read, MarketKeyResolver keys, RedisReader redis) : MarketControllerBase
{
    [HttpGet("world_indices")]
    public async Task<IActionResult> WorldIndices() => LegacyJson(await read.WorldIndicesAsync(ClientToken));

    [HttpGet("non_workingday")]
    public async Task<IActionResult> NonWorkingDay()
    {
        var token = RedisReader.Decode(await redis.StringAsync(
            RedisSource.Legacy, keys.Db0, keys.Key("key_S5G_NONWORKINGDAY"), ClientToken));
        return LegacyJson(ApiEnvelope.Ok((token as JObject)?["Data"]));
    }

    [HttpGet("day_before")]
    public async Task<IActionResult> DayBefore() => LegacyJson(await read.ReadString1Async(
        RedisSource.Sentinel,         keys.Key("key_VHTT") + Exchange.ToUpperInvariant(), false, ClientToken));

    [HttpGet("trading_date")]
    public async Task<IActionResult> TradingDate() => LegacyJson(await read.ReadString0Async(
        RedisSource.Legacy,         "S5G_TRADING_DATE", false, ClientToken));
}
