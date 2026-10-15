namespace DSMarketService.Contracts;

/// <summary>Hợp đồng phản hồi giữ đúng 3 trường Code/Message/Data cho client cũ.</summary>
public sealed class ApiEnvelope
{
    [JsonProperty(Order = 1)] public long Code { get; init; }
    [JsonProperty(Order = 2)] public string Message { get; init; } = "SUCCESS";
    [JsonProperty(Order = 3)] public object? Data { get; init; }
    public static ApiEnvelope Ok(object? data) => new() { Code = 0, Message = "SUCCESS", Data = data };
    public static ApiEnvelope Error(Exception e) => new() { Code = -9998, Message = e.Message, Data = null };
}

public sealed class CandleSeries
{
    public string Symbol { get; set; } = "";
    public List<long> t { get; set; } = new();
    public List<double> o { get; set; } = new();
    public List<double> h { get; set; } = new();
    public List<double> l { get; set; } = new();
    public List<double> c { get; set; } = new();
    public List<long> v { get; set; } = new();
}
