namespace DSMarketService.Infrastructure.Networking;

/// <summary>EnsureNetwork: kiểm tra TCP tới cả hai hệ Redis và những endpoint khác trong cấu hình.</summary>
public static class NetworkBootstrap
{
    public static async Task EnsureNetworkAsync(IConfiguration config, ILogger logger, CancellationToken ct = default)
    {
        if (!config.GetValue("Network:Enabled", true))
            return;

        var endpoints = new List<(string Host, int Port, string Role)>();

        // 1. Redis endpoints
        foreach (var setting in new[]
        {
            "Redis:RedisSentinel",
            "Redis:RedisLegacy"
        })
        {
            var cs = config[setting];

            if (string.IsNullOrWhiteSpace(cs)) 
                throw new InvalidOperationException($"Thiếu cấu hình {setting}.");

            foreach (var ep in ConfigurationOptions.Parse(cs).EndPoints)
            {
                switch (ep)
                {
                    case DnsEndPoint dns: endpoints.Add((dns.Host, dns.Port, setting)); break;

                    case IPEndPoint ip: endpoints.Add((ip.Address.ToString(), ip.Port, setting)); break;
                }
            }
        }

        // 2. Additional endpoints
        foreach (var setting in config.GetSection("Network:Endpoints").GetChildren())
        {
            var host = setting["Host"];

            if (!string.IsNullOrWhiteSpace(host) && int.TryParse(setting["Port"], out var port))
            {
                endpoints.Add(( host, port, "Network:Endpoints"));
            }
        }

        var targets = endpoints.Distinct().ToArray();

        // 3. Log bắt đầu kiểm tra
        SafeLogInformation(logger, "Checking network => {Endpoints}", LogEndPoints(targets));

        int successCount = 0;
        int failedCount = 0;

        // 4. Kiểm tra từng endpoint
        foreach (var ep in targets)
        {
            bool connected = false;

            for (int attempt = 1; attempt <= 3 && !connected; attempt++)
            {
                try
                {
                    using var socket = new TcpClient();

                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);

                    timeout.CancelAfter(TimeSpan.FromSeconds(2));

                    await socket.ConnectAsync(ep.Host, ep.Port, timeout.Token);

                    connected = true;
                    successCount++;

                    // Log kết nối thành công
                    SafeLogInformation(
                        logger,
                        "EnsureNetwork SUCCESS Role={Role} " +
                        "Host={Host} Port={Port} Attempt={Attempt}",
                        ep.Role,
                        ep.Host,
                        ep.Port,
                        attempt);
                }
                catch (Exception ex) when (!ct.IsCancellationRequested && ex is SocketException or OperationCanceledException)
                {
                    try
                    {
                        logger.LogWarning(
                            "EnsureNetwork FAILED Role={Role} " +
                            "Host={Host} Port={Port} Attempt={Attempt}",
                            ep.Role,
                            ep.Host,
                            ep.Port,
                            attempt);
                    }
                    catch
                    {
                        // Kafka logger lỗi không ảnh hưởng network check.
                    }

                    if (attempt < 3)
                        await Task.Delay(300 * attempt, ct);
                }
            }

            if (!connected)
            {
                failedCount++;

                if (config.GetValue("Network:FailFast", false))
                {
                    throw new InvalidOperationException($"EnsureNetwork failed: {ep.Role} -> " + $"{ep.Host}:{ep.Port}");
                }
            }
        }

        // 5. Tổng kết
        SafeLogInformation(
            logger,
            "EnsureNetwork COMPLETED Total={Total} " +
            "Success={Success} Failed={Failed}",
            targets.Length,
            successCount,
            failedCount);
    }

    private static string LogEndPoints(IEnumerable<(string Host, int Port, string Role)> endpoints)
    {
        return string.Join(", ", endpoints.Select(ep => $"{ep.Role}={ep.Host}:{ep.Port}"));
    }

    private static void SafeLogInformation(ILogger logger, string message, params object?[] args)
    {
        try
        {
            logger.LogInformation(message, args);
        }
        catch
        {
            // Không để Kafka logger lỗi ảnh hưởng startup.
        }
    }
}
