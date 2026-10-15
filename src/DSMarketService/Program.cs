var builder = WebApplication.CreateBuilder(args);

// Provider MddsService: cấu hình Manonero.Logger.Kafka tại KafkaLogger:Targets/Rules.
// Không dùng KafkaLoggingProvider/KafkaLogWorker tự viết; đăng ký một lần ở startup.
var appSetting = AppSetting.MapValue(builder.Configuration);
builder.Services.AddSingleton(appSetting);
builder.Services.AddKafkaLogger(appSetting);

// Hai Redis hoàn toàn độc lập: Sentinel cho dữ liệu giá/biểu đồ/thống kê,
// Legacy cho các API cũ như PT, company_name, last_index, trading_date, portfolio.
// RedisReader chỉ đọc được qua RedisSource được chỉ rõ ở từng nghiệp vụ.
builder.Services.AddSingleton<RedisConnections>();
builder.Services.AddSingleton<RedisReader>();
builder.Services.AddSingleton<MarketKeyResolver>();
builder.Services.AddSingleton<MarketReadService>();
builder.Services.AddSingleton<ReferenceDataService>();
builder.Services.AddSingleton<StatisticsService>();
builder.Services.AddSingleton<SqlMarketReader>();
builder.Services.AddSingleton<OracleMarketReader>();
builder.Services.AddSingleton<HistorySeriesService>();
builder.Services.AddControllers();

// Không đặt rate limiter/concurrency limiter ở tầng HTTP. Pool backend vẫn cần timeout.
builder.Services.Configure<HostOptions>(opt => opt.ShutdownTimeout = TimeSpan.FromSeconds(40));

var app = builder.Build();
// Routing trước middleware API_COMPLETED để lấy route template chính xác.
app.UseRouting();
app.UseMiddleware<ApiCompletionLoggingMiddleware>();
app.UseMiddleware<SlowRequestMiddleware>();
app.UseMiddleware<MarketExceptionMiddleware>();
app.MapGet("/", () => Results.Text("DSMarketService .NET 10"));
app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
app.MapGet("/health/ready", (RedisConnections redis) =>
    redis.IsReady ? (IResult)Results.Ok(new { status = "ready" }) : Results.StatusCode(503));

app.MapControllers();

if (app.Environment.IsDevelopment())
{
    app.MapGet("/__debug/exception", () =>
    {
        throw new InvalidOperationException("TEST: Exception from DSMarketService");
    });
}

// EnsureNetwork kiểm tra CẢ Sentinel lẫn Redis thường trước khi kết nối.
await NetworkBootstrap.EnsureNetworkAsync(app.Configuration, app.Logger);
await app.Services.GetRequiredService<RedisConnections>().InitializeAsync(app.Configuration);
await app.RunAsync();
