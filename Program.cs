using Microsoft.Extensions.Configuration;
using PrismaExport.Config;
using PrismaExport.Services;
using Serilog;
using Serilog.Sinks.MSSqlServer;

class Program
{
    static async Task<int> Main(string[] args)
    {
        return await RunExport(args);
    }

    static async Task<int> RunExport(string[] args)
    {
        Console.WriteLine(new string('=', 50));
        Console.WriteLine("  Prisma - CSV / SQL Server Export Utility");
        Console.WriteLine(new string('=', 50));
        Console.WriteLine();

        var rawConfig = BuildConfiguration(args);
        var config    = LoadExportConfig(rawConfig);
        ConfigureLogger(rawConfig);

        Log.Information("Prisma export started.");

        if (!ValidateExportConfig(config))
        {
            return 1;
        }

        bool doCsv      = config.Export.Mode is "csv" or "both";
        bool doSql      = config.Export.Mode is "sql" or "both";
        bool doTruncate = args.Contains("--truncate");

        Console.WriteLine($"Mode:   {config.Export.Mode.ToUpperInvariant()}");
        Console.WriteLine($"Source: PostgreSQL - {config.Postgres.Tables.Count} table(s)");
        if (doCsv)
        {
            Console.WriteLine($"CSV:    {Path.GetFullPath(config.Export.CsvOutputDir)}");
        }
        if (doSql)
        {
            Console.WriteLine($"SQL:    [{config.SqlServer.TargetSchema}]  {config.SqlServer.ConnectionString}");
        }
        Console.WriteLine(new string('-', 50));

        await using var pg = new PgSchemaReader(config.Postgres.ConnectionString);

        using var sqlExporter = doSql
            ? new SqlServerExporter(config.SqlServer.ConnectionString, config.SqlServer.TargetSchema, config.SqlServer.TablePrefix)
            : null;

        try
        {
            if (doSql && sqlExporter != null)
            {
                await sqlExporter.OpenAsync();
            }

            foreach (var table in config.Postgres.Tables)
            {
                Console.WriteLine($"\n[{table}]");

                var columns = await pg.GetColumnsAsync(table);
                if (columns.Count == 0)
                {
                    Log.Warning("No columns found for {Table}. Skipping.", table);
                    continue;
                }

                var rowCount = await pg.GetRowCountAsync(table);
                Console.WriteLine($"  {columns.Count} columns, {rowCount:N0} rows");
                Log.Information("Exporting {Table} - {RowCount:N0} rows.", table, rowCount);

                if (doSql && sqlExporter != null)
                {
                    await sqlExporter.SyncTableSchemaAsync(table, columns);
                    if (doTruncate)
                    {
                        await sqlExporter.TruncateTableAsync(table);
                    }
                }

                if (doCsv)
                {
                    var (csvReader, csvCmd) = await pg.ReadTableDataAsync(table);
                    await using (csvReader) await using (csvCmd)
                    {
                        await CsvExporter.ExportAsync(table, csvReader, config.Export.CsvOutputDir);
                    }
                }

                if (doSql && sqlExporter != null)
                {
                    var (sqlReader, sqlCmd) = await pg.ReadTableDataAsync(table);
                    await using (sqlReader) await using (sqlCmd)
                    {
                        await sqlExporter.BulkInsertAsync(table, sqlReader);
                    }
                }
            }

            Console.WriteLine("\nExport complete.");
            Log.Information("Prisma export complete.");
            return 0;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Export failed: {Message}", ex.Message);
            Console.Error.WriteLine($"\nERROR: {ex.Message}");
            return 1;
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }

    static IConfiguration BuildConfiguration(string[] args) =>
        new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .AddCommandLine(args)
            .Build();

    static ExportConfig LoadExportConfig(IConfiguration config)
    {
        var export = new ExportConfig();
        config.Bind(export);

        // --mode command-line override
        var modeArg = config["mode"];
        if (!string.IsNullOrEmpty(modeArg))
        {
            export.Export.Mode = modeArg.ToLowerInvariant();
        }

        // --tables override: "pp11.jobs,public.acc_job"
        var tablesArg = config["tables"];
        if (!string.IsNullOrEmpty(tablesArg))
        {
            export.Postgres.Tables = tablesArg
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(t =>
                {
                    var parts = t.Split('.');
                    return parts.Length == 2
                        ? new TableRef { Schema = parts[0], Table = parts[1] }
                        : new TableRef { Schema = "public", Table = parts[0] };
                })
                .ToList();
        }

        return export;
    }

    static void ConfigureLogger(IConfiguration config)
    {
        var logDb     = config.GetConnectionString("LogDb");
        var logConfig = new LoggerConfiguration()
            .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}");

        if (!string.IsNullOrEmpty(logDb))
        {
            logConfig.WriteTo.MSSqlServer(logDb, new MSSqlServerSinkOptions { TableName = "Logs", AutoCreateSqlTable = false });
        }

        Log.Logger = logConfig.CreateLogger();
    }

    static bool ValidateExportConfig(ExportConfig config)
    {
        if (string.IsNullOrEmpty(config.Postgres.ConnectionString))
        {
            Log.Fatal("Postgres connection string is required.");
            return false;
        }

        if (config.Postgres.Tables.Count == 0)
        {
            Log.Fatal("No tables configured. Add tables to appsettings.json or use --tables pp11.jobs");
            return false;
        }

        if (config.Export.Mode is "sql" or "both" && string.IsNullOrEmpty(config.SqlServer.ConnectionString))
        {
            Log.Fatal("SQL Server connection string required for sql/both mode.");
            return false;
        }

        return true;
    }

}
