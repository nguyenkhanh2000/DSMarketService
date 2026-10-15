namespace DSMarketService.Middleware;

/// <summary>
/// Xử lý lỗi thống nhất cho các Controller; không lặp try/catch cho từng action.
/// Giữ nguyên kiểu phản hồi lỗi Code/Message/Data và content-type text/plain của API cũ.
/// </summary>
public sealed class MarketExceptionMiddleware(RequestDelegate next, ILogger<MarketExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // Client đã đóng kết nối: không tạo response/log lỗi vô nghĩa.
        }
        catch (Exception ex)
        {
            // Trạng thái nghiệp vụ là lỗi mặc dù API legacy vẫn trả HTTP 200.
            // Middleware API_COMPLETED đọc cờ này để phân biệt lỗi nghiệp vụ dù HTTP 200.
            context.Items[ApiCompletionLoggingMiddleware.ExceptionHandledKey] = true;
            // Log tên lỗi và đường dẫn, không đẩy connection string hoặc dữ liệu giá lên Kafka.

            try
            {
                var request = context.Request;

                var inputParams = RequestLogFormatter.Format(
                    request,
                    maxParameters: 32,
                    maxValueLength: 256,
                    maxTotalLength: 4096);

                var bodyLength = RequestLogFormatter.GetRequestBodyLength(request);

                logger.LogError(
                    "API_ERROR Api={Api} Method={Method} " +
                    "InputParams={InputParams} " +
                    "BodyLength={BodyLength} " +
                    "ExceptionType={ExceptionType} " +
                    "TraceId={TraceId}",
                    request.Path.Value,
                    request.Method,
                    inputParams,
                    bodyLength,
                    ex.GetType().Name,
                    context.TraceIdentifier);
            }
            catch (Exception)
            {
                // Không để lỗi ghi log ảnh hưởng response API.
            }

            if (context.Response.HasStarted)
                throw; // Không được ghi JSON mới khi response đã bắt đầu truyền.

            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status200OK; // Giữ hành vi lớp endpoint .NET 8 trước đó.
            context.Response.ContentType = "text/plain; charset=utf-8";
            var json = JsonConvert.SerializeObject(ApiEnvelope.Error(ex));
            context.Items[ApiCompletionLoggingMiddleware.ResponseBodyLengthKey] =
                (long)Encoding.UTF8.GetByteCount(json);
            await context.Response.WriteAsync(json, context.RequestAborted);
        }
    }
}
