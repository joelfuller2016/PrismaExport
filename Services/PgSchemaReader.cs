using Npgsql;
using PrismaExport.Config;

namespace PrismaExport.Services;

public class PgSchemaReader : IAsyncDisposable
{
    private readonly NpgsqlDataSource dataSource;

    public PgSchemaReader(string connectionString)
    {
        dataSource = NpgsqlDataSource.Create(connectionString);
    }

    public async Task<List<TableRef>> DiscoverTablesAsync(string schema)
    {
        var tables = new List<TableRef>();
        var sql = @"SELECT table_schema, table_name
                     FROM information_schema.tables
                     WHERE table_schema = @schema AND table_type = 'BASE TABLE'
                     ORDER BY table_name";

        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("@schema", schema);
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            tables.Add(new TableRef
            {
                Schema = reader.GetString(0),
                Table = reader.GetString(1)
            });
        }

        return tables;
    }

    public async Task<List<ColumnDef>> GetColumnsAsync(TableRef table)
    {
        var columns = new List<ColumnDef>();
        var sql = @"SELECT column_name, data_type, udt_name,
                           character_maximum_length,
                           numeric_precision, numeric_scale,
                           is_nullable, ordinal_position
                    FROM information_schema.columns
                    WHERE table_schema = @schema AND table_name = @table
                    ORDER BY ordinal_position";

        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("@schema", table.Schema);
        cmd.Parameters.AddWithValue("@table", table.Table);
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            columns.Add(new ColumnDef
            {
                Name = reader.GetString(0),
                DataType = reader.GetString(1),
                UdtName = reader.GetString(2),
                MaxLength = reader.IsDBNull(3) ? null : reader.GetInt32(3),
                Precision = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                Scale = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                IsNullable = reader.GetString(6) == "YES",
                OrdinalPosition = reader.GetInt32(7)
            });
        }

        return columns;
    }

    public async Task<(NpgsqlDataReader Reader, NpgsqlCommand Command)> ReadTableDataAsync(TableRef table)
    {
        var sql = $"SELECT * FROM {table.QualifiedName}";
        var cmd = dataSource.CreateCommand(sql);
        var reader = await cmd.ExecuteReaderAsync();
        return (reader, cmd);
    }

    public async Task<long> GetRowCountAsync(TableRef table)
    {
        var sql = $"SELECT COUNT(*) FROM {table.QualifiedName}";
        await using var cmd = dataSource.CreateCommand(sql);
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt64(result);
    }

    public async ValueTask DisposeAsync()
    {
        await dataSource.DisposeAsync();
    }
}

public class ColumnDef
{
    public string Name { get; set; } = "";
    public string DataType { get; set; } = "";
    public string UdtName { get; set; } = "";
    public int? MaxLength { get; set; }
    public int? Precision { get; set; }
    public int? Scale { get; set; }
    public bool IsNullable { get; set; }
    public int OrdinalPosition { get; set; }

    public string ToSqlServerType() => TypeMapper.MapToSqlServer(DataType, MaxLength, Precision, Scale);
}
