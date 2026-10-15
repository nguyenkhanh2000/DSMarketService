namespace DSMarketService.Infrastructure.Database;

/// <summary>Đọc RefCursor bất đồng bộ; tránh OracleDataAdapter.Fill đồng bộ trên ThreadPool.</summary>
public sealed class OracleMarketReader(IConfiguration config)
{
    public async Task<DataSet> WarrantyInfoAsync(string code, CancellationToken ct)
    {
        var cs = config["Oracle:ConnectionString_trd"] ?? throw new InvalidOperationException("Oracle connection missing");
        await using var connection = new OracleConnection(cs);
        await connection.OpenAsync(ct);
        await using var cmd = connection.CreateCommand();
        cmd.BindByName = true;
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.CommandText = "trd.sptrd_get_cw_info";
        cmd.CommandTimeout = config.GetValue("Oracle:CommandTimeoutSeconds", 8);
        cmd.Parameters.Add(new OracleParameter("pcw", OracleDbType.Varchar2, 50, code, ParameterDirection.Input));
        cmd.Parameters.Add(new OracleParameter("rcw", OracleDbType.RefCursor, ParameterDirection.Output));
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        // DataSet được giữ để Newtonsoft.Json xuất ra cùng cấu trúc bảng như response cũ.
        var set = new DataSet();
        var index = 0;
        do
        {
            var table = new DataTable("Table" + (index == 0 ? "" : index.ToString()));
            for (int i = 0; i < reader.FieldCount; i++)
                table.Columns.Add(reader.GetName(i), reader.GetFieldType(i));
            while (await reader.ReadAsync(ct))
            {
                var values = new object[reader.FieldCount];
                reader.GetValues(values);
                table.Rows.Add(values);
            }
            set.Tables.Add(table);
            index++;
        } while (await reader.NextResultAsync(ct));
        return set;
    }
}
