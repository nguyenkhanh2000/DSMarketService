namespace DSMarketService.Middleware;

/// <summary>
/// Một log Information /ds_market API tại điểm hoàn tất (API_COMPLETED).
/// InputParams: route + query parameters, được URL-encode, giới hạn kích thước và che secrets.
/// BodyLength: Content-Length của REQUEST (byte), -1 nếu không xác định; không đọc body.
/// ResponseBodyLength: byte UTF-8 đã serialize ở LegacyJson/LegacyRaw, hoặc Content-Length,
/// -1 nếu không xác định. Không sao chép response body để đếm byte.
/// </summary>
public sealed class ApiCompletionLoggingMiddleware(
    RequestDelegate next,
    ILogger<ApiCompletionLoggingMiddleware> logger,
    IConfiguration config)
{
    public static readonly object ExceptionHandledKey = new();
    public static readonly object BusinessFailureKey = new();
    public static readonly object ResponseBodyLengthKey = new();
    private static readonly EventId CompletedEvent = new(1001, "API_COMPLETED");

    private readonly bool _infoEnabled = config.GetValue("Diagnostics:ApiInfoLogging:Enabled", true);
    private readonly int _maxParams = Math.Clamp(config.GetValue("Diagnostics:ApiInfoLogging:MaxParameters", 32), 1, 128);
    private readonly int _maxParamValueLength = Math.Clamp(config.GetValue("Diagnostics:ApiInfoLogging:MaxParameterValueLength", 256), 1, 2048);
    private readonly int _maxParamsLength = Math.Clamp(config.GetValue("Diagnostics:ApiInfoLogging:MaxInputParamsLength", 4096), 128, 16384);

    public async Task InvokeAsync(HttpContext context)
    {
        // UseRouting chạy trước middleware này. Không ghi log thống kê /health hoặc /.
        var template = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText;
        if (string.IsNullOrEmpty(template) ||
            !template.StartsWith("ds_market/", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        var api = template["ds_market/".Length..].ToLowerInvariant();
        var start = Stopwatch.GetTimestamp();
        var inputParams = "-";
        var bodyLength = -1L;

        if (_infoEnabled)
        {
            // Đọc query/route trước khi chạy controller; không consume Request.Body.
            // Thất bại khi chuẩn bị log không được ảnh hưởng API nghiệp vụ.
            try
            {
                inputParams = RequestLogFormatter.Format(
                    context.Request, _maxParams, _maxParamValueLength, _maxParamsLength);
                bodyLength = RequestLogFormatter.GetRequestBodyLength(context.Request);
            }
            catch (Exception)
            {
                inputParams = "unavailable";
            }
        }

        try
        {
            await next(context);
        }
        finally
        {
            if (_infoEnabled)
            {
                var elapsedMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                var statusCode = context.Response.StatusCode;
                var outcome = context.RequestAborted.IsCancellationRequested
                    ? "cancelled"
                    : context.Items.ContainsKey(ExceptionHandledKey) ||
                      context.Items.ContainsKey(BusinessFailureKey) || statusCode >= 500
                        ? "error" : "completed";

                // Các Controller kế thừa MarketControllerBase đều đặt ResponseBodyLengthKey.
                // Khi response không phải LegacyJson/Raw, dùng header nếu có, nếu không = -1.
                long responseBodyLength = context.Items.TryGetValue(ResponseBodyLengthKey, out var responseBytes)
                    && responseBytes is long byteCount
                        ? byteCount
                        : context.Response.ContentLength ?? -1L;

                try
                {
                    logger.LogInformation(CompletedEvent,
                        "API_COMPLETED Api={Api} Method={Method} StatusCode={StatusCode} DurationMs={DurationMs} Outcome={Outcome} InputParams={InputParams} BodyLength={BodyLength} ResponseBodyLength={ResponseBodyLength} TraceId={TraceId}",
                        api, context.Request.Method, statusCode, (long)Math.Round(elapsedMs), outcome,
                        inputParams, bodyLength, responseBodyLength, context.TraceIdentifier);
                }
                catch (Exception)
                {
                    // Exception đồng bộ từ logging provider không được thay đổi kết quả HTTP.
                    // Kafka delivery/retry bất đồng bộ vẫn do Manonero.Logger.Kafka chịu trách nhiệm.
                }
            }
        }
    }
}
