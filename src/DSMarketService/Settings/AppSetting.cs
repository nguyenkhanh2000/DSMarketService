namespace DSMarketService.Settings;

/// <summary>
/// Ánh xạ riêng cho logging, không thay đổi cấu hình nghiệp vụ Redis/SQL/Oracle.
/// LoggingSetting là section IConfiguration mà Manonero.Logger.Kafka.AddKafka(...) sử dụng.
/// </summary>
public sealed class AppSetting
{
    public bool AllowConsoleLog { get; init; } = true;
    public required IConfigurationSection LoggingSetting { get; init; }

    public static AppSetting MapValue(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var kafka = configuration.GetSection("KafkaLogger");
        if (!kafka.Exists())
            throw new InvalidOperationException("Missing KafkaLogger configuration section.");

        return new AppSetting
        {
            AllowConsoleLog = configuration.GetValue("AllowConsoleLog", true),
            LoggingSetting = kafka
        };
    }
}
