namespace DSMarketService.Controllers;

/// <summary>
/// Lớp nền chỉ cung cấp tiện ích đọc tham số và trả JSON.
/// Không dùng model binding ép kiểu theo culture của server để tránh làm đổi
/// hợp đồng query parameter (đặc biệt from, to, resolution).
/// </summary>
public abstract class MarketControllerBase : ControllerBase
{
    protected CancellationToken ClientToken => HttpContext.RequestAborted;

    protected string Q(string name, string fallback = "") =>
        Request.Query.TryGetValue(name, out StringValues values) ? values.ToString() : fallback;

    protected int QInt(string name, int fallback = 0) =>
        int.TryParse(Q(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result : fallback;

    protected long QLong(string name, long fallback = 0) =>
        long.TryParse(Q(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result : fallback;

    protected double QDouble(string name, double fallback = 0) =>
        double.TryParse(Q(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
            ? result : fallback;

    protected string Exchange => Q("exchange");

    /// <summary>
    /// Legacy trả JSON dưới dạng text/plain. Không đổi sang Ok(object),
    /// vì MVC mặc định có thể serialize khác Newtonsoft (null, number, thứ tự field).
    /// </summary>
    protected ContentResult LegacyJson(object? value)
    {
        // API có thể dùng HTTP 200 ngay cả khi Code nghiệp vụ != 0.
        // Đánh dấu cho middleware INFO để thống kê chính xác mà KHÔNG đổi JSON.
        if (value is ApiEnvelope { Code: not 0 })
            HttpContext.Items[ApiCompletionLoggingMiddleware.BusinessFailureKey] = true;
        var payload = JsonConvert.SerializeObject(value);
        // ResponseBodyLength = byte UTF-8; Output = Data.Count / Data.Length,
        // KHÔNG giữ JSON body trong HttpContext.Items để ghi Kafka.
        HttpContext.Items[ApiCompletionLoggingMiddleware.ResponseBodyLengthKey] =
            (long)Encoding.UTF8.GetByteCount(payload);
        HttpContext.Items[ApiCompletionLoggingMiddleware.OutputLengthKey] =
            GetDataLengthForLog(value, payload.Length);
        return Content(payload, "text/plain; charset=utf-8");
    }

    /// <summary>API last_index là raw JSON, KHÔNG bọc Code/Message/Data.</summary>
    protected ContentResult LegacyRaw(string? value)
    {
        var payload = value ?? "";
        HttpContext.Items[ApiCompletionLoggingMiddleware.ResponseBodyLengthKey] =
            (long)Encoding.UTF8.GetByteCount(payload);
        HttpContext.Items[ApiCompletionLoggingMiddleware.OutputLengthKey] = (long)payload.Length;
        return Content(payload, "text/plain; charset=utf-8");
    }

    /// <summary>
    /// Với dữ liệu dạng array/list: số phần tử như List.Count ở DSMarket cũ.
    /// Với string: số ký tự (Length). Với object không có Count: độ dài
    /// JSON đã serialize; không serialize lại và không ghi nội dung vào Kafka.
    /// </summary>
    private static long GetDataLengthForLog(object? value, int serializedLength)
    {
        var data = value is ApiEnvelope envelope ? envelope.Data : value;
        return data switch
        {
            null => 0L,
            JArray array => array.Count,
            Array array => array.LongLength,
            System.Collections.ICollection list => list.Count,
            JValue jsonString when jsonString.Type == JTokenType.String =>
                (long)(jsonString.Value<string>()?.Length ?? 0),
            string text => text.Length,
            _ => serializedLength
        };
    }
}
