namespace PrismaExport.Config;

public class ExportConfig
{
    public PostgresConfig Postgres { get; set; } = new();
    public SqlServerConfig SqlServer { get; set; } = new();
    public ExportOptions Export { get; set; } = new();
}

public class PostgresConfig
{
    public string ConnectionString { get; set; } = "";
    public List<TableRef> Tables { get; set; } = [];
}

public class TableRef
{
    public string Schema { get; set; } = "";
    public string Table { get; set; } = "";

    /// <summary>Quoted identifier for Postgres queries: "schema"."table"</summary>
    public string QualifiedName => $"\"{ Schema}\".\"{ Table}\"";

    /// <summary>Display-friendly name: schema.table</summary>
    public override string ToString() => $"{Schema}.{Table}";
}

public class SqlServerConfig
{
    public string ConnectionString { get; set; } = "";
    public string TargetSchema { get; set; } = "prisma";
    public string TablePrefix { get; set; } = "";
}

public class ExportOptions
{
    /// <summary>"csv", "sql", or "both"</summary>
    public string Mode { get; set; } = "csv";
    public string CsvOutputDir { get; set; } = "./exports";
}
