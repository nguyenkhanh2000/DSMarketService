namespace DSMarketService.Infrastructure.Networking;

/// <summary>EnsureNetwork: kiểm tra TCP tới cả hai hệ Redis và những endpoint khác trong cấu hình.</summary>
public static class NetworkBootstrap
{
    public static async Task EnsureNetworkAsync(IConfiguration config, ILogger logger, CancellationToken ct = default)
    {
        if (!config.GetValue("Network:Enabled", true)) return;

        var endpoints = new List<(string Host, int Port, string Role)>();
        foreach (var setting in new[] { "Redis:RedisSentinel", "Redis:RedisLegacy" })
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
        foreach (var setting in config.GetSection("Network:Endpoints").GetChildren())
        {
            var host = setting["Host"];
            if (!string.IsNullOrWhiteSpace(host) && int.TryParse(setting["Port"], out var port))
                endpoints.Add((host, port, "Network:Endpoints"));
        }

        // Bất kỳ Redis nào mất kết nối đều được đưa vào log bằng tên vai trò;
        // tuyệt đối không log connection string vì có thể chứa password.
        foreach (var ep in endpoints.Distinct())
        {
            var connected = false;
            for (var attempt = 1; attempt <= 3 && !connected; attempt++)
            {
                try
                {
                    using var socket = new TcpClient();
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    timeout.CancelAfter(TimeSpan.FromSeconds(2));
                    await socket.ConnectAsync(ep.Host, ep.Port, timeout.Token);
                    connected = true;
                }
                catch (Exception ex) when (ex is SocketException or OperationCanceledException)
                {
                    // Diagnostic logging không được làm hỏng logic kiểm tra mạng khi Kafka down.
                    try
                    {
                        logger.LogWarning("EnsureNetwork: {Role} không kết nối được tới {Host}:{Port}, lần {Attempt}",
                            ep.Role, ep.Host, ep.Port, attempt);
                    }
                    catch (Exception)
                    {
                        // Không log ngược bằng ILogger vì chính logging provider đang lỗi.
                    }
                    if (attempt < 3) await Task.Delay(300 * attempt, ct);
                }
            }
            if (!connected && config.GetValue("Network:FailFast", false))
                throw new InvalidOperationException($"EnsureNetwork failed: {ep.Role} -> {ep.Host}:{ep.Port}");
        }
    }
}
