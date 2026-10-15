namespace DSMarketService.Features;

/// <summary>5 loại statistics tương ứng 5 Stored Procedure; tên SP lấy cấu hình tin cậy.</summary>
public sealed class StatisticsService(SqlMarketReader sql, IConfiguration config)
{
    private static readonly string[] Procedures =
    {
        "Statistics_Index", "Statistics_Price", "Statistics_Order", "Statistics_Foregin_NM", "Statistics_Foregin_PT"
    };
    public async Task<ApiEnvelope> ReadAsync(int direction, int center, string begin, string end,
        int page, int pageSize, string? code, int type, CancellationToken ct)
    {
        if (direction is < 0 or > 4) throw new ArgumentException("Directional không hợp lệ");
        var sp = config["SQL:" + Procedures[direction]] ?? throw new InvalidOperationException("Missing SP config");
        var parameters = new DynamicParameters();
        parameters.Add("@Center", center, DbType.Int32);
        parameters.Add("@BeginDate", begin, DbType.String);
        parameters.Add("@EndDate", end, DbType.String);
        parameters.Add("@SelectedPage", page, DbType.Int32);
        parameters.Add("@PageSize", pageSize, DbType.Int32);
        if (direction != 0)
        {
            parameters.Add("@Code", code, DbType.String);
            parameters.Add("@Type", type, DbType.Int32);
        }
        return ApiEnvelope.Ok(await sql.ReadMultipleAsync(sp, parameters, ct));
    }
}
