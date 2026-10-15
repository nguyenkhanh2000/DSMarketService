namespace DSMarketService.Infrastructure.Database;

/// <summary>SQL Server: mở connection theo thao tác, nhờ SqlClient pooling tái sử dụng socket vật lý.</summary>
public sealed class SqlMarketReader(IConfiguration configuration)
{
    public async Task<List<List<dynamic>>> ReadMultipleAsync(string procedure, DynamicParameters parameters, CancellationToken ct)
    {
        // Không nối dữ liệu đầu vào vào tên stored procedure hoặc SQL literal.
        var cs = configuration["SQL:Connection64"] ?? throw new InvalidOperationException("SQL:Connection64 missing");
        await using var connection = new SqlConnection(cs);
        await connection.OpenAsync(ct);
        var timeout = configuration.GetValue("SQL:CommandTimeoutSeconds", 8);
        var command = new CommandDefinition(procedure, parameters, commandType: CommandType.StoredProcedure,
            commandTimeout: timeout, cancellationToken: ct);
        using var grid = await connection.QueryMultipleAsync(command);
        var results = new List<List<dynamic>>(4);
        while (!grid.IsConsumed)
            results.Add((await grid.ReadAsync<dynamic>()).ToList());
        return results;
    }
}
