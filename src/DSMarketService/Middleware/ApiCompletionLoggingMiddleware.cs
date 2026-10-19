namespace DSMarketService.Middleware;

/// <summary>
/// Một log Information /ds_market API tại điểm hoàn tất (API_COMPLETED).
/// InputParams: route + query parameters, được URL-encode, giới hạn kích thước và che secrets.
/// BodyLength: Content-Length của REQUEST (byte), -1 nếu không xác định; không đọc body.
/// ResponseBodyLength: byte UTF-8 đã serialize ở LegacyJson/LegacyRaw, hoặc Content-Length,
/// -1 nếu không xác định. Không sao chép response body để đếm byte.
/// Input: giống InputParams. Output: chỉ ghi Count (array/list) hoặc Length (string),
/// không ghi nội dung JSON. ResponseBodyLength tiếp tục là số byte UTF-8.
/// </summary>
public sealed class ApiCompletionLoggingMiddleware(
    RequestDelegate next,
    ILogger<ApiCompletionLoggingMiddleware> logger,
    IConfiguration config)
{
    public static readonly object ExceptionHandledKey = new();
    public static readonly object BusinessFailureKey = new();
    public static readonly object ResponseBodyLengthKey = new();
    // Output là số lượng phần tử (hoặc độ dài chuỗi), không phải JSON content.
    public static readonly object OutputLengthKey = new();
    private static readonly EventId CompletedEvent = new(1001, "API_COMPLETED");

    private readonly bool _infoEnabled = config.GetValue("Diagnostics:ApiInfoLogging:Enabled", true);
    private readonly int _maxParams = Math.Clamp(config.GetValue("Diagnostics:ApiInfoLogging:MaxParameters", 32), 1, 128);
    private readonly int _maxParamValueLength = Math.Clamp(config.GetValue("Diagnostics:ApiInfoLogging:MaxParameterValueLength", 256), 1, 2048);
    private readonly int _maxParamsLength = Math.Clamp(config.GetValue("Diagnostics:ApiInfoLogging:MaxInputParamsLength", 4096), 128, 16384);
    private readonly string _module = config["KafkaLogger:AppName"] ?? "DSMarketService";

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
                    // URL không chứa query raw: tham số được log ở Input có che secrets.
                    var request = context.Request;
                    var url = RequestLogMetadata.SafeText(
                        request.PathBase.ToString() + request.Path.ToString(), 512);
                    var host = RequestLogMetadata.SafeText(request.Host.Value, 256);
                    var ipServer = context.Connection.LocalIpAddress?.ToString() ?? "-";
                    var ipClient = context.Connection.RemoteIpAddress?.ToString() ?? "-";
                    // Chỉ ghi giá trị số. Controller đặt OutputLengthKey từ Data.Count
                    // hoặc string.Length; không lưu lại JSON body để ghi Kafka.
                    var outputLength = context.Items.TryGetValue(OutputLengthKey, out var countValue)
                        && countValue is long count
                            ? count
                            : -1L;
                    logger.LogInformation(CompletedEvent,
                        "API_COMPLETED URL={URL} Method={Method} Host={Host} TypeLog={TypeLog} " +
                        "IPServer={IPServer} IPClient={IPClient} Module={Module} " +
                        "Input={Input} Output={Output} " +
                        "Api={Api} StatusCode={StatusCode} DurationMs={DurationMs} " +
                        "Outcome={Outcome} InputParams={InputParams} " +
                        "BodyLength={BodyLength} ResponseBodyLength={ResponseBodyLength} TraceId={TraceId}",
                        url, request.Method, host, "API", ipServer, ipClient,
                        RequestLogMetadata.SafeText(_module, 128), inputParams, outputLength,
                        api, statusCode, (long)Math.Round(elapsedMs), outcome,
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
