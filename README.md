# PrismaExport

PostgreSQL to CSV/SQL Server export utility built with .NET 10.

## Build
```bash
dotnet build
```

## Run
```bash
dotnet run -- --mode sql --tables pp11.jobs
```

### Options
- `--mode csv|sql|both` - Export mode (default from appsettings.json)
- `--tables schema.table,...` - Override table list
- `--truncate` - Truncate target tables before insert
