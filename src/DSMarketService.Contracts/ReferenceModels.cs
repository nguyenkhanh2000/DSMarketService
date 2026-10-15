namespace DSMarketService.Contracts;

/// <summary>DTO công ty: đủ default 0/null để serializing tương đương lớp StockCompany cũ.</summary>
public sealed class StockCompany
{
    public int cpnyID { get; set; }
    public string? stock_code { get; set; }
    public int CodeID { get; set; }
    public string? Ex { get; set; }
    public string? name_vn { get; set; }
    public string? name_en { get; set; }
    public string? stock_type { get; set; }
}

/// <summary>Phản hồi chỉ số thế giới: chỉ 10 trường cũ, tất cả đều string hoặc null.</summary>
public sealed class WorldIndexQuote
{
    public string? Symbol { get; set; }
    public string? Close { get; set; }
    public string? Change { get; set; }
    public string? ChangePct { get; set; }
    public string? Open { get; set; }
    public string? High { get; set; }
    public string? Low { get; set; }
    public string? Volume { get; set; }
    public string? Volume_O { get; set; }
    public string? Volume_AC { get; set; }
}
