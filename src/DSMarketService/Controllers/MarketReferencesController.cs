namespace DSMarketService.Controllers;

/// <summary>API dữ liệu công ty và chứng quyền (Oracle).</summary>
[Route("ds_market")]
public sealed class MarketReferencesController(OracleMarketReader oracle,
    ReferenceDataService references) : MarketControllerBase
{
    [HttpGet("cw_info")]
    public async Task<IActionResult> CoveredWarrants() => LegacyJson(
        ApiEnvelope.Ok(await oracle.WarrantyInfoAsync(Q("stockCode").ToUpperInvariant(), ClientToken)));

    [HttpGet("company_name")]
    public async Task<IActionResult> CompanyName()
    {
        var (result, etag) = await references.CompaniesAsync(ClientToken);
        if (etag != null) Response.Headers.ETag = "\"" + etag + "\"";
        return LegacyJson(result);
    }
}
