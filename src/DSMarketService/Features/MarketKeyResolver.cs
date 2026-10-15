namespace DSMarketService.Features;

/// <summary>
/// Định danh Redis key/DB là hợp đồng dữ liệu của producer hiện tại.
/// Không chuẩn hóa khác đi nếu chưa di chuyển dữ liệu Redis phía upstream.
/// </summary>
public sealed class MarketKeyResolver(IConfiguration config)
{
    public int Db0 => config.GetValue("Redis:Db0", 0);
    public int Db1 => config.GetValue("Redis:Db1", 1);
    public int Db3 => config.GetValue("Redis:Db3", 8);
    public string Key(string setting) => config["Redis:" + setting] ?? throw new InvalidOperationException("Thiếu Redis:" + setting);
    public string Portfolio(string exchange) => exchange switch
    {
        "hnx" or "hnxupcomindex" => Key("key_s5g_hnx"),
        "hnxds" => Key("key_s5g_hnxds"),
        "vnx" => Key("key_s5g_vnx"),
        "ministry" => Key("key_s5g_ministry"),
        "cw" => Key("key_s5g_cw"),
        _ => throw new ArgumentException("exchange không hợp lệ", nameof(exchange))
    };
}
