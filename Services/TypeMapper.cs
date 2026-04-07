namespace PrismaExport.Services;

public static class TypeMapper
{
    private static readonly Dictionary<string, string> PgToSqlServer = new(StringComparer.OrdinalIgnoreCase)
    {
        // Character types
        ["text"] = "nvarchar(max)",
        ["character varying"] = "nvarchar({0})",
        ["varchar"] = "nvarchar({0})",
        ["character"] = "nchar({0})",
        ["char"] = "nchar({0})",
        ["name"] = "nvarchar(128)",
        ["citext"] = "nvarchar(max)",

        // Numeric types
        ["smallint"] = "smallint",
        ["int2"] = "smallint",
        ["integer"] = "int",
        ["int4"] = "int",
        ["bigint"] = "bigint",
        ["int8"] = "bigint",
        ["numeric"] = "decimal({0},{1})",
        ["decimal"] = "decimal({0},{1})",
        ["real"] = "real",
        ["float4"] = "real",
        ["double precision"] = "float",
        ["float8"] = "float",
        ["serial"] = "int",
        ["bigserial"] = "bigint",
        ["smallserial"] = "smallint",
        ["money"] = "money",

        // Boolean
        ["boolean"] = "bit",
        ["bool"] = "bit",

        // Date/Time
        ["date"] = "date",
        ["time"] = "time",
        ["time without time zone"] = "time",
        ["time with time zone"] = "datetimeoffset",
        ["timetz"] = "datetimeoffset",
        ["timestamp"] = "datetime2",
        ["timestamp without time zone"] = "datetime2",
        ["timestamp with time zone"] = "datetimeoffset",
        ["timestamptz"] = "datetimeoffset",
        ["interval"] = "nvarchar(100)",

        // Binary
        ["bytea"] = "varbinary(max)",

        // UUID
        ["uuid"] = "uniqueidentifier",

        // JSON
        ["json"] = "nvarchar(max)",
        ["jsonb"] = "nvarchar(max)",

        // XML
        ["xml"] = "xml",

        // Network types
        ["inet"] = "nvarchar(50)",
        ["cidr"] = "nvarchar(50)",
        ["macaddr"] = "nvarchar(20)",

        // Geometric (store as text)
        ["point"] = "nvarchar(100)",
        ["line"] = "nvarchar(200)",
        ["lseg"] = "nvarchar(200)",
        ["box"] = "nvarchar(200)",
        ["path"] = "nvarchar(max)",
        ["polygon"] = "nvarchar(max)",
        ["circle"] = "nvarchar(200)",

        // Other
        ["oid"] = "bigint",
        ["bit"] = "binary(1)",
        ["bit varying"] = "varbinary({0})",
        ["varbit"] = "varbinary({0})",
        ["tsvector"] = "nvarchar(max)",
        ["tsquery"] = "nvarchar(max)",
    };

    public static string MapToSqlServer(string pgType, int? maxLength, int? precision, int? scale)
    {
        var typeLower = pgType.ToLowerInvariant();

        // Array types -> store as nvarchar(max)
        if (typeLower.EndsWith("[]") || typeLower.StartsWith("_"))
        {
            return "nvarchar(max)";
        }

        if (!PgToSqlServer.TryGetValue(typeLower, out var template))
        {
            return "nvarchar(max)";
        }

        if (template.Contains("{0}") && template.Contains("{1}"))
        {
            var p = precision ?? 18;
            var s = scale ?? 2;
            return string.Format(template, p, s);
        }

        if (template.Contains("{0}"))
        {
            var size = maxLength > 0 ? maxLength.Value : 255;
            if (size > 4000)
            {
                return template.Replace("({0})", "(max)");
            }
            return string.Format(template, size);
        }

        return template;
    }
}
