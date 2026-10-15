using DSMarketService.Settings;
using Manonero.Logger.Kafka;

namespace DSMarketService.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Đăng ký Manonero.Logger.Kafka giống cách MddsService đăng ký logger.
    /// AddKafka nhận IConfigurationSection "KafkaLogger" (Targets/Rules/BootstrapServers/AppName).
    /// Đăng ký duy nhất một lần khi ứng dụng khởi động, tuyệt đối không tạo mỗi request.
    /// </summary>
    public static IServiceCollection AddKafkaLogger(this IServiceCollection services, AppSetting appSetting)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(appSetting);
        bool isAllowedConsoleLog = appSetting.AllowConsoleLog;
        services.AddLogging(config =>
        {
            if (!isAllowedConsoleLog)
            {
                config.ClearProviders();
            }

            config.AddKafka(appSetting.LoggingSetting);
        });

        return services;
    }
}
