namespace DSMarketService.Extensions
{
    /// <summary>
    /// Manonero.Logger.Kafka's TargetKafkaJson (khi AllowFieldsFromMessage = true) tách field ra khỏi
    /// message text đã format bằng regex \0Key=Value\0 - nó không đọc structured properties {PropName}
    /// của ILogger. Nối chuỗi này vào cuối message template (vd "{Message}{Fields}") để field query
    /// được trên Elastic; AutoRemoveFieldsFromMessage sẽ tự xoá phần này khỏi field Message trên Kafka JSON.
    /// </summary>
    internal static class LogFieldExtensions
    {
        private const char FieldMarker = '\0';
        internal static string BuildFieldSuffix(params (string Key, object? Value)[] fields)
        {
            if (fields.Length == 0) return string.Empty;

            var sb = new StringBuilder(fields.Length * 48);
            foreach (var (key, value) in fields)
            {
                if (string.IsNullOrWhiteSpace(key) ||
                    key.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_'))
                {
                    throw new ArgumentException("Invalid Kafka logging field key.", nameof(fields));
                }

                var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "null";
                // Avoid injecting an extra NUL-delimited field or breaking downstream JSON.
                // These are log metadata only; raw response data is never sent here.
                text = text.Replace("\0", " ", StringComparison.Ordinal)
                           .Replace("\r", " ", StringComparison.Ordinal)
                           .Replace("\n", " ", StringComparison.Ordinal)
                           .Replace("\t", " ", StringComparison.Ordinal)
                           .Replace("\\", "%5C", StringComparison.Ordinal)
                           .Replace("\"", "%22", StringComparison.Ordinal);

                sb.Append(FieldMarker)
                  .Append(key)
                  .Append('=')
                  .Append(text)
                  .Append(FieldMarker)
                  .Append(", ");
            }

            return sb.ToString();
        }
    }
}
