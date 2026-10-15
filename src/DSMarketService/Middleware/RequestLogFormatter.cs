namespace DSMarketService.Middleware;

/// <summary>
/// Biểu diễn tham số API cho Kafka log mà không đọc nội dung body.
/// Percent-encode key/value để không bị ngắt dòng, inject delimiter hay phá template Kafka.
/// Chặn những khóa thường chứa mật khẩu/token; giới hạn giá trị và tổng log.
/// </summary>
internal static class RequestLogFormatter
{
    internal static long GetRequestBodyLength(HttpRequest request)
    {
        if (request.ContentLength is long bodyLength) return bodyLength;

        // GET/HEAD không có body là 0. Với phương thức có thể chứa body nhưng
        // thiếu Content-Length (ví dụ chunked/HTTP2), -1 = KHÔNG BIẾT, không đoán 0.
        return HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method)
            ? 0L : -1L;
    }

    internal static string Format(HttpRequest request, int maxParameters, int maxValueLength, int maxTotalLength)
    {
        var result = new StringBuilder();
        var paramCount = 0;
        var truncated = false;

        // RouteValues hiện chưa có biến route, nhưng hỗ trợ sẵn nếu bổ sung endpoint sau này.
        foreach (var route in request.RouteValues)
        {
            if (!TryAppend("route." + route.Key, Convert.ToString(route.Value, CultureInfo.InvariantCulture) ?? ""))
                break;
        }

        if (!truncated)
        {
            // StringValues.ToString() giữ cùng cách đọc multi-value với MarketControllerBase.Q().
            foreach (var query in request.Query)
            {
                if (!TryAppend(query.Key, query.Value.ToString())) break;
            }
        }

        if (truncated)
        {
            const string suffix = "&__truncated=true";
            if (result.Length == 0)
            {
                return "__truncated=true";
            }
            if (result.Length + suffix.Length <= maxTotalLength) result.Append(suffix);
        }

        return result.Length == 0 ? "-" : result.ToString();

        bool TryAppend(string name, string value)
        {
            if (paramCount >= maxParameters)
            {
                truncated = true;
                return false;
            }

            // Không cấp phát một bản sao giá trị nhạy cảm vào chuỗi log.
            var sensitive = IsSensitive(name);
            if (sensitive) value = "[REDACTED]";

            // Hạn chế trước khi URL-encode để tránh phải tạo chuỗi lớn vì input không tin cậy.
            bool shortened = !sensitive && value.Length > maxValueLength;
            if (shortened)
            {
                var len = maxValueLength;
                if (len > 0 && len < value.Length && char.IsHighSurrogate(value[len - 1]) &&
                    char.IsLowSurrogate(value[len])) len--;
                value = value[..len];
            }

            var entry = Uri.EscapeDataString(name) + "=" + Uri.EscapeDataString(value) +
                (shortened ? "..." : "");
            var extra = entry.Length + (result.Length == 0 ? 0 : 1);
            if (result.Length + extra > maxTotalLength)
            {
                truncated = true;
                return false;
            }

            if (result.Length > 0) result.Append('&');
            result.Append(entry);
            paramCount++;
            return true;
        }
    }

    private static bool IsSensitive(string name)
    {
        // Chỉ loại dấu _/- để bắt cả access_token, accessToken, api-key...
        var normalized = name.Replace("_", "", StringComparison.Ordinal)
            .Replace("-", "", StringComparison.Ordinal)
            .ToLowerInvariant();
        return normalized.Contains("password", StringComparison.Ordinal) ||
               normalized.Contains("passwd", StringComparison.Ordinal) ||
               normalized.Contains("secret", StringComparison.Ordinal) ||
               normalized.Contains("token", StringComparison.Ordinal) ||
               normalized.Contains("authorization", StringComparison.Ordinal) ||
               normalized.Contains("credential", StringComparison.Ordinal) ||
               normalized.Contains("cookie", StringComparison.Ordinal) ||
               normalized is "auth" or "apikey" or "apiaccesskey" or "pwd" or "jwt" or "sessionid" or "signature";
    }
}
