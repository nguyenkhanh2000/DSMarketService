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
        // Độ dài thực tế của JSON phản hồi theo UTF-8, không giữ thêm bản sao body.
        HttpContext.Items[ApiCompletionLoggingMiddleware.ResponseBodyLengthKey] =
            (long)Encoding.UTF8.GetByteCount(payload);
        return Content(payload, "text/plain; charset=utf-8");
    }

    /// <summary>API last_index là raw JSON, KHÔNG bọc Code/Message/Data.</summary>
    protected ContentResult LegacyRaw(string? value)
    {
        var payload = value ?? "";
        HttpContext.Items[ApiCompletionLoggingMiddleware.ResponseBodyLengthKey] =
            (long)Encoding.UTF8.GetByteCount(payload);
        return Content(payload, "text/plain; charset=utf-8");
    }
}
