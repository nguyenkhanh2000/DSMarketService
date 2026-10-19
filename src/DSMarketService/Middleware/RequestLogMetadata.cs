namespace DSMarketService.Middleware;

/// <summary>
/// Chuẩn hóa thông tin đưa vào Kafka message: bounded, 1 dòng, không làm hỏng template.
/// Output là bản JSON đã serialize và có thể bị cắt ngắn để không tăng tải Kafka.
/// KHÔNG dùng để serialize response gửi client.
/// </summary>
internal static class RequestLogMetadata
{
    internal static string SafeText(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return "-";
        var len = Math.Min(value.Length, maxLength);
        if (len < value.Length && len > 0 && char.IsHighSurrogate(value[len - 1]) &&
            char.IsLowSurrogate(value[len])) len--;
        var builder = new StringBuilder(len + 32);
        for (var i = 0; i < len; i++)
            AppendSafe(builder, value[i]);
        if (len < value.Length) builder.Append("...[TRUNCATED]");
        return builder.ToString();
    }

    internal static string SafeOutput(string? json, int maxLength)
    {
        if (json is null) return "-";
        // Dùng cùng một cơ chế giới hạn/escape với metadata. Không tạo bản sao JSON đầy đủ.
        return SafeText(json, maxLength);
    }

    private static void AppendSafe(StringBuilder builder, char c)
    {
        switch (c)
        {
            case '\r': builder.Append("\\r"); break;
            case '\n': builder.Append("\\n"); break;
            case '\t': builder.Append("\\t"); break;
            // Kafka template hiện bao message trong dấu nháy đơn; tránh phá format downstream.
            case '\'': builder.Append("\\u0027"); break;
            default:
                if (char.IsControl(c)) builder.Append(' ');
                else builder.Append(c);
                break;
        }
    }
}
