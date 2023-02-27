﻿using Atheon.Attributes;
using Atheon.DapperExtensions;
using Atheon.Models.Database.Destiny;
using Atheon.Models.Database.Destiny.Profiles;
using Atheon.Models.Database.Sqlite;
using Atheon.Options;
using Atheon.Services.Interfaces;
using Dapper;
using DotNetBungieAPI.Models.Destiny.Definitions.Collectibles;
using DotNetBungieAPI.Models.Destiny.Definitions.Metrics;
using DotNetBungieAPI.Models.Destiny.Definitions.Progressions;
using DotNetBungieAPI.Models.Destiny.Definitions.Records;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;
using System.Reflection;
using System.Text;

namespace Atheon.Services.Db.Sqlite;

public class SqliteDbBootstrap : IDbBootstrap
{
    private readonly ILogger<SqliteDbBootstrap> _logger;
    private readonly IOptions<DatabaseOptions> _databaseOptions;
    private readonly IDbAccess _dbAccess;
    private readonly IOptions<JsonOptions> _jsonOptions;

    public SqliteDbBootstrap(
        ILogger<SqliteDbBootstrap> logger,
        IOptions<DatabaseOptions> databaseOptions,
        IDbAccess dbAccess,
        IOptions<JsonOptions> jsonOptions)
    {
        _logger = logger;
        _databaseOptions = databaseOptions;
        _dbAccess = dbAccess;
        _jsonOptions = jsonOptions;
    }

    private void RegisterDapperMappings()
    {
        _logger.LogDebug("Registering DB mappings...");

        var assemblyTypes = Assembly.GetAssembly(GetType())!.GetTypes();
        var automappedTypes = assemblyTypes.Where(x => x.GetCustomAttribute<DapperAutomapAttribute>() is not null);

        foreach (var type in automappedTypes)
        {
            SqlMapper.SetTypeMap(
                type: type,
                new CustomPropertyTypeMap(
                    type,
                    (type, columnName) =>
                    {
                        return type
                            .GetProperties()
                            .FirstOrDefault(prop =>
                                prop.GetCustomAttributes(false)
                                .OfType<AutoColumnAttribute>()
                                .Any(attr => attr.ColumnName == columnName));
                    }));
        }

        RegisterJsonHandler<DefinitionTrackSettings<DestinyMetricDefinition>>();
        RegisterJsonHandler<DefinitionTrackSettings<DestinyRecordDefinition>>();
        RegisterJsonHandler<DefinitionTrackSettings<DestinyCollectibleDefinition>>();
        RegisterJsonHandler<DefinitionTrackSettings<DestinyProgressionDefinition>>();
        RegisterJsonHandler<HashSet<long>>();
        RegisterJsonHandler<HashSet<uint>>();
        RegisterJsonHandler<Dictionary<uint, DestinyRecordDbModel>>();
        RegisterJsonHandler<Dictionary<uint, DestinyProgressionDbModel>>();
        RegisterJsonHandler<Dictionary<string, string>>();
    }

    public async Task InitialiseDb(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Initialising database...");

        RegisterDapperMappings();

        await CreateTablesFromSettings();

        await CreateTablesFromModels();
    }

    private async Task CreateTablesFromModels()
    {
        var tableSettings = FindAllAutogeneratedTables();

        foreach (var (tableMetadata, tableColumns) in tableSettings)
        {
            var tableSettingsData = new DatabaseTableEntry()
            {
                Columns = tableColumns.Select(x => new DatabaseTableColumn()
                {
                    Name = x.ColumnName,
                    NotNull = x.NotNull,
                    PrimaryKey = x.IsPrimaryKey,
                    Type = new Dictionary<string, string>() { { DatabaseOptions.SqliteKey, x.SqliteType } }
                }).ToList()
            };

            if (await TableNotExists(tableMetadata.Name))
            {              
                await CreateTable(tableMetadata.Name, tableSettingsData);
            }
            else
            {
                var columns = await GetTableColumns(tableMetadata.Name);

                var columnMapping = new Dictionary<DatabaseTableColumn, ColumnInfo>();

                foreach (var column in tableSettingsData.Columns)
                {
                    var mappedColumn = columns.FirstOrDefault(x => x.Name == column.Name);

                    if (mappedColumn is not null)
                    {
                        columnMapping.Add(column, mappedColumn);

                        if (!mappedColumn.IsEqualTo(column))
                        {
                            // well fuck we have an issue there
                        }
                    }
                    else
                    {
                        await _dbAccess.ExecuteAsync($"ALTER TABLE {tableMetadata.Name} ADD COLUMN {column.FormatForCreateQuery(DatabaseOptions.SqliteKey)}");
                    }
                }
            }
        }
    }

    private async Task CreateTablesFromSettings()
    {
        var settings = _databaseOptions.Value;

        foreach (var (tableName, tableSettings) in settings.Tables)
        {
            if (await TableNotExists(tableName))
            {
                await CreateTable(tableName, tableSettings);
            }
            else
            {
                var columns = await GetTableColumns(tableName);

                var columnMapping = new Dictionary<DatabaseTableColumn, ColumnInfo>();

                foreach (var column in tableSettings.Columns)
                {
                    var mappedColumn = columns.FirstOrDefault(x => x.Name == column.Name);

                    if (mappedColumn is not null)
                    {
                        columnMapping.Add(column, mappedColumn);

                        if (!mappedColumn.IsEqualTo(column))
                        {
                            // well fuck we have an issue there
                        }
                    }
                    else
                    {
                        await _dbAccess.ExecuteAsync($"ALTER TABLE {tableName} ADD COLUMN {column.FormatForCreateQuery(DatabaseOptions.SqliteKey)}");
                    }
                }
            }
        }
    }

    private async Task<bool> TableNotExists(string tableName)
    {
        var result = await _dbAccess.QueryFirstOrDefaultAsync<int?>("SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = @Name", new { Name = tableName });

        return result != 1;
    }

    private async Task<List<ColumnInfo>> GetTableColumns(string tableName)
    {
        var result = await _dbAccess.QueryAsync<ColumnInfo>($"PRAGMA table_info({tableName})");

        return result.ToList();
    }

    private async Task CreateTable(string tableName, DatabaseTableEntry tableSettings)
    {
        var sb = new StringBuilder();

        if (tableSettings.Columns.Count(x => x.PrimaryKey == true) > 1)
        {
            sb.Append($"CREATE TABLE {tableName}(");

            sb.AppendJoin(", ", tableSettings.Columns.Select(x => x.FormatForCreateQueryWithoutPK(DatabaseOptions.SqliteKey)));

            sb.Append($", PRIMARY KEY ({string.Join(',', tableSettings.Columns.Where(x => x.PrimaryKey == true).Select(x => x.Name))})");

            sb.Append(");");
        }
        else
        {

            sb.Append($"CREATE TABLE {tableName}(");

            sb.AppendJoin(", ", tableSettings.Columns.Select(x => x.FormatForCreateQuery(DatabaseOptions.SqliteKey)));

            sb.Append(");");
        }

        await _dbAccess.ExecuteAsync(sb.ToString());
    }

    private void RegisterJsonHandler<THandledType>()
    {
        SqlMapper.AddTypeHandler(typeof(THandledType), new JsonTypeHandler<THandledType>(_jsonOptions.Value.SerializerOptions));
    }

    private Dictionary<AutoTableAttribute, List<AutoColumnAttribute>> FindAllAutogeneratedTables()
    {
        var assemblyTypes = Assembly.GetAssembly(GetType())!.GetTypes();
        var automappedTypes = assemblyTypes.Where(x => x.GetCustomAttribute<AutoTableAttribute>() is not null);

        var typeMappings = new Dictionary<AutoTableAttribute, List<AutoColumnAttribute>>();

        foreach (var type in automappedTypes)
        {
            var tableMetadata = type.GetCustomAttribute<AutoTableAttribute>()!;
            var columnProperties = type.GetProperties().Where(x => x.GetCustomAttribute<AutoColumnAttribute>() is not null);

            var props = new List<AutoColumnAttribute>();
            foreach (var property in columnProperties)
            {
                var metadata = property.GetCustomAttribute<AutoColumnAttribute>()!;
                props.Add(metadata);
            }

            typeMappings.Add(tableMetadata, props);
        }

        return typeMappings;
    }
}
