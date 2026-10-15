using DSMarketService.Middleware;

static class Program
{
    private static int _assertCount;

    public static async Task Main()
    {
        var request = new DefaultHttpContext().Request;
        request.Method = "GET";
        request.QueryString = new QueryString("?stockCode=VPB&from=0&to=-1&access_token=secret-value");
        var paramString = RequestLogFormatter.Format(request, 32, 256, 4096);
        Assert(paramString.Contains("stockCode=VPB", StringComparison.Ordinal), "stockCode included");
        Assert(paramString.Contains("from=0", StringComparison.Ordinal), "from included");
        Assert(paramString.Contains("to=-1", StringComparison.Ordinal), "to included");
        Assert(!paramString.Contains("secret-value", StringComparison.Ordinal), "token redacted");
        Assert(paramString.Contains("access_token=%5BREDACTED%5D", StringComparison.Ordinal), "redaction marker");
        Assert(RequestLogFormatter.GetRequestBodyLength(request) == 0, "GET without body = 0");

        request.Method = "POST";
        request.ContentLength = 456;
        Assert(RequestLogFormatter.GetRequestBodyLength(request) == 456, "Content-Length bytes");
        request.ContentLength = null;
        Assert(RequestLogFormatter.GetRequestBodyLength(request) == -1, "unknown body length = -1");

        request.QueryString = new QueryString("?x=hello%20world&password=abc&y=" + new string('a', 50));
        paramString = RequestLogFormatter.Format(request, 2, 4, 4096);
        Assert(paramString.Contains("x=hell...", StringComparison.Ordinal), "value bounded");
        Assert(paramString.Contains("password=%5BREDACTED%5D", StringComparison.Ordinal), "password redacted");
        Assert(paramString.Contains("__truncated=true", StringComparison.Ordinal), "parameter count bounded");

        var logger = new FakeLogger();
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Diagnostics:ApiInfoLogging:Enabled"] = "true"
        }).Build();
        var ctx = BuildApiContext("ds_market/matching", "?stockCode=VPB&from=0&to=-1");
        var middleware = new ApiCompletionLoggingMiddleware(http =>
        {
            http.Items[ApiCompletionLoggingMiddleware.ResponseBodyLengthKey] = 123L;
            return Task.CompletedTask;
        }, logger, cfg);
        await middleware.InvokeAsync(ctx);
        Assert(logger.Entries.Count == 1, "exactly one API_COMPLETED record");
        Assert(logger.Entries[0].Contains("InputParams=stockCode=VPB", StringComparison.Ordinal), "middleware input params");
        Assert(logger.Entries[0].Contains("BodyLength=0", StringComparison.Ordinal), "middleware request body length");
        Assert(logger.Entries[0].Contains("ResponseBodyLength=123", StringComparison.Ordinal), "middleware response body length");
        Assert(logger.EventIds[0].Id == 1001, "Kafka event id unchanged");

        var errorContext = BuildApiContext("ds_market/matching", "?stockCode=VPB");
        var errorMiddleware = new ApiCompletionLoggingMiddleware(http =>
        {
            http.Items[ApiCompletionLoggingMiddleware.BusinessFailureKey] = true;
            http.Response.ContentLength = 17;
            return Task.CompletedTask;
        }, logger, cfg);
        await errorMiddleware.InvokeAsync(errorContext);
        Assert(logger.Entries[1].Contains("Outcome=error", StringComparison.Ordinal), "HTTP 200 business error tracked");
        Assert(logger.Entries[1].Contains("ResponseBodyLength=17", StringComparison.Ordinal), "fallback content length");

        var failingLogger = new FakeLogger { Throw = true };
        bool businessRan = false;
        var failSafe = new ApiCompletionLoggingMiddleware(_ =>
        {
            businessRan = true;
            return Task.CompletedTask;
        }, failingLogger, cfg);
        await failSafe.InvokeAsync(BuildApiContext("ds_market/matching", "?stockCode=VPB"));
        Assert(businessRan, "logger exception does not abort a request");

        var healthContext = BuildApiContext("health/live", "");
        await middleware.InvokeAsync(healthContext);
        Assert(logger.Entries.Count == 2, "health endpoints excluded");
        Console.WriteLine($"PASS: {_assertCount} logging assertions");
    }

    private static DefaultHttpContext BuildApiContext(string template, string query)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = "/" + template;
        context.Request.QueryString = new QueryString(query);
        context.SetEndpoint(new RouteEndpoint(_ => Task.CompletedTask,
            RoutePatternFactory.Parse(template), 0,
            new EndpointMetadataCollection(), template));
        return context;
    }

    private static void Assert(bool condition, string detail)
    {
        if (!condition) throw new InvalidOperationException("FAILED: " + detail);
        _assertCount++;
    }

    private sealed class FakeLogger : ILogger<ApiCompletionLoggingMiddleware>
    {
        public List<string> Entries { get; } = [];
        public List<EventId> EventIds { get; } = [];
        public bool Throw { get; init; }
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => Scope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (Throw) throw new InvalidOperationException("simulated logger outage");
            EventIds.Add(eventId);
            Entries.Add(formatter(state, exception));
        }
    }

    private sealed class Scope : IDisposable
    {
        public static readonly Scope Instance = new();
        public void Dispose() { }
    }
}
