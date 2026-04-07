using System.Text;
using Npgsql;
using PrismaExport.Config;
using Serilog;

namespace PrismaExport.Services;

public static class CsvExporter
{
    public static async Task ExportAsync(TableRef table, NpgsqlDataReader reader, string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        var filePath = Path.Combine(outputDir, $"{table.Schema}_{table.Table}.csv");

        try
        {
            await using var writer = new StreamWriter(filePath, false, new UTF8Encoding(true));

            var headers = new string[reader.FieldCount];
            for (int i = 0; i < reader.FieldCount; i++)
            {
                headers[i] = reader.GetName(i);
            }

            await writer.WriteLineAsync(string.Join(",", headers));

            long rowCount = 0;
            while (await reader.ReadAsync())
            {
                var values = new string[reader.FieldCount];
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    if (reader.IsDBNull(i))
                    {
                        values[i] = "";
                    }
                    else
                    {
                        string val;
                        if (reader.GetDataTypeName(i) == "interval")
                        {
                            val = reader.GetTimeSpan(i).ToString();
                        }
                        else
                        {
                            val = reader.GetValue(i)?.ToString() ?? "";
                        }
                        values[i] = EscapeCsvField(val);
                    }
                }

                await writer.WriteLineAsync(string.Join(",", values));
                rowCount++;

                if (rowCount % 10000 == 0)
                {
                    Console.Write($"\r  {table} - {rowCount:N0} rows written...");
                }
            }

            Console.WriteLine($"\r  {table} - {rowCount:N0} rows -> {filePath}");
            Log.Information("{Table} -> CSV - {RowCount:N0} rows written to {FilePath}.", table, rowCount, filePath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "CSV export failed for {Table} to {FilePath}: {Message}", table, filePath, ex.Message);
            throw;
        }
    }

    private static string EscapeCsvField(string field)
    {
        if (field.Contains(',') || field.Contains('"') || field.Contains('\n') || field.Contains('\r'))
        {
            return $"\"{field.Replace("\"", "\"\"")}\"";
        }
        return field;
    }
}
