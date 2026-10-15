namespace DSMarketService.Features;

/// <summary>Danh sách mã chứng khoán + ETag. Không tính SHA256 trên request chart/realtime.</summary>
public sealed class ReferenceDataService(RedisReader redis, MarketKeyResolver keys, IConfiguration cfg)
{
    public async Task<(ApiEnvelope Result, string? Etag)> CompaniesAsync(CancellationToken ct)
    {
        var token = RedisReader.Decode(await redis.StringAsync(RedisSource.Legacy, keys.Db0, keys.Key("key_S5G_COMPANY_NAME"), ct));
        var array = (token as JObject)?["Data"] as JArray ?? new JArray();
        var companies = array.ToObject<List<StockCompany>>() ?? new List<StockCompany>();
        var symbols = (cfg["SymbolInfo:Symbols"] ?? "").Split(',');
        var exchanges = (cfg["SymbolInfo:Ex"] ?? "").Split(',');
        var vn = (cfg["SymbolInfo:Des_VN"] ?? "").Split(',');
        var en = (cfg["SymbolInfo:Des_EN"] ?? "").Split(',');
        for (int i = 0; i < symbols.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(symbols[i])) continue;
            if (i >= exchanges.Length || i >= vn.Length || i >= en.Length)
                throw new InvalidOperationException("Các mảng SymbolInfo có số phần tử khác nhau");
            companies.Add(new StockCompany
            {
                stock_code = symbols[i], Ex = exchanges[i], name_vn = vn[i], name_en = en[i], stock_type = "I"
            });
        }
        var serialized = JsonConvert.SerializeObject(companies);
        // Legacy GenerateETag sử dụng thuật toán riêng; phải xác nhận trước khi bật 304.
        var etag = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(serialized)));
        return (ApiEnvelope.Ok(companies), etag);
    }
}
