using System.Data;
using Microsoft.Data.SqlClient;
using Npgsql;
using PrismaExport.Config;
using Serilog;

namespace PrismaExport.Services;

public class SqlServerExporter : IDisposable
{
    private readonly SqlConnection conn;
    private readonly string targetSchema;
    private readonly string tablePrefix;

    public SqlServerExporter(string connectionString, string targetSchema, string tablePrefix = "")
    {
        this.targetSchema = targetSchema;
        this.tablePrefix = tablePrefix;
        conn = new SqlConnection(connectionString);
    }

    // Target table name pattern: {prefix}{schema}_{table} e.g. EXPORT_pp11_jobs
    private string ResolveTargetName(TableRef sourceTable)
        => $"{tablePrefix}{sourceTable.Schema}_{sourceTable.Table}";

    public async Task OpenAsync()
    {
        await conn.OpenAsync();
        await EnsureSchemaExistsAsync();
    }

    private async Task EnsureSchemaExistsAsync()
    {
        var sql = $@"IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = @schema)
                     EXEC('CREATE SCHEMA [{targetSchema}]')";
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@schema", targetSchema);
        await cmd.ExecuteNonQueryAsync();
        Log.Information("Schema [{Schema}] verified.", targetSchema);
    }

    public async Task SyncTableSchemaAsync(TableRef sourceTable, List<ColumnDef> pgColumns)
    {
        var sqlName = ResolveTargetName(sourceTable);
        var targetTable = $"[{targetSchema}].[{sqlName}]";

        if (!await TableExistsAsync(sqlName))
        {
            await CreateTableAsync(sqlName, pgColumns);
            Log.Information("Created SQL Server table {Schema}.{Table}.", targetSchema, sqlName);
            Console.WriteLine($"  Created table {targetTable}");
        }
        else
        {
            var added = await AddMissingColumnsAsync(sqlName, pgColumns);
            if (added > 0)
            {
                Log.Information("Added {Count} missing column(s) to {Schema}.{Table}.", added, targetSchema, sqlName);
                Console.WriteLine($"  Added {added} missing column(s) to {targetTable}");
            }
            else
            {
                Console.WriteLine($"  Table {targetTable} schema is up to date.");
            }
        }
    }

    private async Task<bool> TableExistsAsync(string tableName)
    {
        var sql = @"SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES
                    WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @table";
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@schema", targetSchema);
        cmd.Parameters.AddWithValue("@table", tableName);
        var count = (int)(await cmd.ExecuteScalarAsync())!;
        return count > 0;
    }

    private async Task CreateTableAsync(string tableName, List<ColumnDef> columns)
    {
        var colDefs = columns.Select(c =>
        {
            var sqlType = c.ToSqlServerType();
            var nullable = c.IsNullable ? "NULL" : "NOT NULL";
            return $"    [{c.Name}] {sqlType} {nullable}";
        });

        var sql = $"CREATE TABLE [{targetSchema}].[{tableName}] (\n{string.Join(",\n", colDefs)}\n)";
        await using var cmd = new SqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<int> AddMissingColumnsAsync(string tableName, List<ColumnDef> pgColumns)
    {
        var existingCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sql = @"SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @table";
        await using (var cmd = new SqlCommand(sql, conn))
        {
            cmd.Parameters.AddWithValue("@schema", targetSchema);
            cmd.Parameters.AddWithValue("@table", tableName);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                existingCols.Add(reader.GetString(0));
            }
        }

        int added = 0;
        foreach (var col in pgColumns)
        {
            if (existingCols.Contains(col.Name))
                continue;

            var alterSql = $"ALTER TABLE [{targetSchema}].[{tableName}] ADD [{col.Name}] {col.ToSqlServerType()} NULL";
            await using var cmd = new SqlCommand(alterSql, conn);
            await cmd.ExecuteNonQueryAsync();
            Console.WriteLine($"    + Added column [{col.Name}] ({col.ToSqlServerType()})");
            added++;
        }

        return added;
    }

    public async Task BulkInsertAsync(TableRef sourceTable, NpgsqlDataReader pgReader)
    {
        var sqlName = ResolveTargetName(sourceTable);
        var targetTable = $"[{targetSchema}].[{sqlName}]";

        // Build a DataTable matching the Postgres columns
        var dt = new DataTable();
        for (int i = 0; i < pgReader.FieldCount; i++)
        {
            dt.Columns.Add(pgReader.GetName(i), typeof(object));
        }

        long rowCount = 0;
        const int batchSize = 5000;

        try
        {
            while (await pgReader.ReadAsync())
            {
                var row = dt.NewRow();
                for (int i = 0; i < pgReader.FieldCount; i++)
                {
                    row[i] = pgReader.IsDBNull(i) ? DBNull.Value :
                              pgReader.GetDataTypeName(i) == "interval" ? (object)pgReader.GetTimeSpan(i).ToString() :
                              pgReader.GetValue(i);
                }
                dt.Rows.Add(row);
                rowCount++;

                if (dt.Rows.Count >= batchSize)
                {
                    await FlushBatchAsync(dt, sqlName);
                    Console.Write($"\r  {sourceTable} -> SQL Server [{targetSchema}].[{sqlName}] - {rowCount:N0} rows inserted...");
                    dt.Rows.Clear();
                }
            }

            if (dt.Rows.Count > 0)
            {
                await FlushBatchAsync(dt, sqlName);
            }

            Console.WriteLine($"\r  {sourceTable} -> SQL Server [{targetSchema}].[{sqlName}] - {rowCount:N0} rows inserted.           ");
            Log.Information("{Source} -> [{Schema}].[{SqlTable}] - {RowCount:N0} rows inserted.", sourceTable, targetSchema, sqlName, rowCount);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Bulk insert failed for {Source} -> {Target} at row {RowCount}: {Message}",
                sourceTable, targetTable, rowCount, ex.Message);
            throw;
        }
    }

    private async Task FlushBatchAsync(DataTable dt, string tableName)
    {
        using var bulkCopy = new SqlBulkCopy(conn)
        {
            DestinationTableName = $"[{targetSchema}].[{tableName}]",
            BulkCopyTimeout = 600
        };

        foreach (DataColumn col in dt.Columns)
        {
            bulkCopy.ColumnMappings.Add(col.ColumnName, col.ColumnName);
        }

        await bulkCopy.WriteToServerAsync(dt);
    }

    public async Task TruncateTableAsync(TableRef table)
    {
        var sqlName = ResolveTargetName(table);
        var sql = $"TRUNCATE TABLE [{targetSchema}].[{sqlName}]";
        try
        {
            await using var cmd = new SqlCommand(sql, conn);
            await cmd.ExecuteNonQueryAsync();
            Log.Information("Truncated [{Schema}].[{Table}].", targetSchema, sqlName);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Truncate failed for [{Schema}].[{Table}]: {Message}", targetSchema, sqlName, ex.Message);
            throw;
        }
    }

    public void Dispose()
    {
        conn.Dispose();
    }
}
