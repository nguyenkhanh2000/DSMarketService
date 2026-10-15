namespace DSMarketService.Middleware;

/// <summary>Không log từng request thành công. Chỉ cảnh báo request quá chậm.</summary>
public sealed class SlowRequestMiddleware(RequestDelegate next, ILogger<SlowRequestMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var start = Stopwatch.GetTimestamp();
        try { await next(context); }
        finally
        {
            var duration = Stopwatch.GetElapsedTime(start);
            if (duration.TotalMilliseconds >= 1000 && !context.RequestAborted.IsCancellationRequested)
            {
                try
                {
                    logger.LogWarning("Slow endpoint {Path} took {ElapsedMs} ms",
                        context.Request.Path, (long)duration.TotalMilliseconds);
                }
                catch (Exception)
                {
                    // Không được throw từ finally chỉ vì lỗi ghi log.
                    // Best effort: không để lỗi logger phá vỡ response.
                }
            }
        }
    }
}
