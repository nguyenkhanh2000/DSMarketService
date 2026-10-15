namespace DSMarketService.Controllers;

/// <summary>Các API thống kê thanh khoản, độ rộng, dẫn dắt và danh mục thị trường.</summary>
[Route("ds_market")]
public sealed class MarketStatisticsController(MarketReadService read, StatisticsService statistics) : MarketControllerBase
{
    [HttpGet("statistics")]
    public async Task<IActionResult> Statistics() => LegacyJson(await statistics.ReadAsync(
        QInt("Directional", 0), QInt("Center", 0), Q("BeginDate"), Q("EndDate"),
        QInt("SelectedPage", 0), QInt("PageSize", 0), Q("Code"), QInt("Type", 0), ClientToken));

    [HttpGet("liquidity")]
    public async Task<IActionResult> Liquidity() => LegacyJson(await read.ReadSeries1Async(RedisSource.Sentinel, 
        "TKTT:" + Q("type") + ":" + Exchange + ":" + QInt("dayNumber", 0),
        QLong("from", 0), QLong("to", -1), false, ClientToken));

    [HttpGet("width")]
    public async Task<IActionResult> MarketWidth() => LegacyJson(await read.ReadSeries1Async(RedisSource.Sentinel, 
        "DRTT:" + Exchange, QLong("from", 0), QLong("to", -1), false, ClientToken));

    [HttpGet("leader")]
    public async Task<IActionResult> Leader() => LegacyJson(await read.ReadString1Async(RedisSource.Sentinel, 
        "DDTT:" + Exchange.ToUpperInvariant(), true, ClientToken));

    [HttpGet("portfolio")]
    public async Task<IActionResult> Portfolio() => LegacyJson(await read.PortfolioAsync(Exchange, ClientToken));
}
