using DevExpress.ExpressApp;
using DevExpress.ExpressApp.DC;
using DevExpress.Persistent.Base;
using Microsoft.Data.SqlClient;
using Serilog;
using XafSecureSearch.Module.BusinessObjects;

namespace XafSecureSearch.Module.Services;

public class SearchDtoRegistry
{
    private static readonly Lazy<SearchDtoRegistry> _instance = new(() => new SearchDtoRegistry());
    public static SearchDtoRegistry Instance => _instance.Value;

    private static readonly ILogger _log = Log.ForContext<SearchDtoRegistry>();

    private readonly Dictionary<int, RegistryEntry> _entries = new();
    private readonly SearchDtoCompiler _compiler = new();
    private readonly object _lock = new();
    private readonly Dictionary<string, int> _entityTypeIndex = new(StringComparer.OrdinalIgnoreCase);

    private SearchDtoRegistry() { }

    /// <summary>
    /// Compiles all active search configurations directly from the database.
    /// Called from Module.Setup() BEFORE model generation so types get proper IModelClass nodes.
    /// Returns the set of compiled DTO full type names (used to clean orphaned model diffs).
    /// </summary>
    public HashSet<string> CompileFromDatabase(string connectionString, ModuleBase module)
    {
        var compiledTypeNames = new HashSet<string>();
        _log.Information("CompileFromDatabase: connectionString present = {Present}", !string.IsNullOrWhiteSpace(connectionString));
        if (string.IsNullOrWhiteSpace(connectionString)) return compiledTypeNames;

        // Only compile once — subsequent Module.Setup() calls (per-session in Blazor) must reuse existing types.
        lock (_lock)
        {
            if (_entries.Count > 0)
            {
                _log.Information("CompileFromDatabase: already compiled {Count} type(s), skipping", _entries.Count);
                return _entries.Values.Select(e => e.DtoType.FullName).ToHashSet();
            }
        }

        try
        {
            List<SearchConfiguration> configs;
            try
            {
                configs = LoadConfigsViaAdoNet(connectionString);
                _log.Information("CompileFromDatabase: found {Count} active config(s)", configs.Count);
            }
            catch (Exception ex)
            {
                _log.Error(ex, "CompileFromDatabase: DB query failed");
                return compiledTypeNames;
            }

            foreach (var config in configs)
            {
                _log.Information("Compiling '{Name}' → {EntityType} ({FieldCount} fields)", config.Name, config.TargetEntityType, config.Fields.Count);
                if (config.Fields.Count == 0) { _log.Warning("Skipped '{Name}': no fields", config.Name); continue; }

                var result = _compiler.Compile(config);
                if (!result.Success)
                {
                    _log.Error("Compilation FAILED for '{Name}': {Errors}", config.Name, string.Join("; ", result.Errors));
                    Tracing.Tracer.LogError($"Search DTO compilation failed for '{config.Name}': {string.Join("; ", result.Errors)}");
                    continue;
                }
                _log.Information("Compiled OK → {DtoType}", result.DtoType?.FullName);

                lock (_lock)
                {
                    var entry = new RegistryEntry
                    {
                        ConfigurationId = config.ID,
                        DtoType = result.DtoType,
                        Source = result.Source,
                        TargetEntityType = config.TargetEntityType
                    };

                    _entries[config.ID] = entry;
                    _entityTypeIndex[config.TargetEntityType] = config.ID;

                    XafTypesInfo.Instance.RegisterEntity(result.DtoType);
                    module.AdditionalExportedTypes.Add(result.DtoType);
                }

                compiledTypeNames.Add(result.DtoType.FullName);
            }

            if (compiledTypeNames.Count > 0)
                _log.Information("Compiled {Count} search panel(s) from database", compiledTypeNames.Count);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "CompileFromDatabase failed");
        }

        return compiledTypeNames;
    }

    private static List<SearchConfiguration> LoadConfigsViaAdoNet(string connectionString)
    {
        var configs = new Dictionary<int, SearchConfiguration>();

        using var conn = new SqlConnection(connectionString);
        conn.Open();

        // Load active configurations
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT ID, Name, TargetEntityType, IsActive FROM SearchConfigurations WHERE IsActive = 1 AND TargetEntityType IS NOT NULL";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var config = new SearchConfiguration
                {
                    ID = reader.GetInt32(0),
                    Name = reader.IsDBNull(1) ? null : reader.GetString(1),
                    TargetEntityType = reader.GetString(2),
                    IsActive = true
                };
                configs[config.ID] = config;
            }
        }

        if (configs.Count == 0) return new List<SearchConfiguration>();

        // Load fields for those configurations
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"SELECT ID, SearchConfigurationId, PropertyName, PropertyTypeName, DisplayName,
                                       UseExactMatch, UseRangeFilter, SortOrder, IsReferenceProperty, ReferencedTypeName
                                FROM SearchFields
                                WHERE SearchConfigurationId IN (SELECT ID FROM SearchConfigurations WHERE IsActive = 1 AND TargetEntityType IS NOT NULL)
                                ORDER BY SortOrder";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var configId = reader.GetInt32(1);
                if (!configs.TryGetValue(configId, out var config)) continue;

                var field = new SearchField
                {
                    ID = reader.GetInt32(0),
                    SearchConfigurationId = configId,
                    PropertyName = reader.IsDBNull(2) ? null : reader.GetString(2),
                    PropertyTypeName = reader.IsDBNull(3) ? null : reader.GetString(3),
                    DisplayName = reader.IsDBNull(4) ? null : reader.GetString(4),
                    UseExactMatch = reader.GetBoolean(5),
                    UseRangeFilter = reader.GetBoolean(6),
                    SortOrder = reader.GetInt32(7),
                    IsReferenceProperty = reader.GetBoolean(8),
                    ReferencedTypeName = reader.IsDBNull(9) ? null : reader.GetString(9)
                };
                config.Fields.Add(field);
            }
        }

        return configs.Values.ToList();
    }

    /// <summary>
    /// Compiles a single configuration and registers the DTO type.
    /// Used for manual compile during a session (requires restart to activate).
    /// </summary>
    public CompilationResult CompileAndRegister(SearchConfiguration config, ModuleBase module)
    {
        var result = _compiler.Compile(config);
        if (!result.Success)
        {
            Tracing.Tracer.LogError($"Search DTO compilation failed for '{config.Name}': {string.Join("; ", result.Errors)}");
            return result;
        }

        lock (_lock)
        {
            if (_entries.TryGetValue(config.ID, out var oldEntry))
            {
                module.AdditionalExportedTypes.Remove(oldEntry.DtoType);
                _entityTypeIndex.Remove(oldEntry.TargetEntityType);
            }

            var entry = new RegistryEntry
            {
                ConfigurationId = config.ID,
                DtoType = result.DtoType,
                Source = result.Source,
                TargetEntityType = config.TargetEntityType
            };

            _entries[config.ID] = entry;
            _entityTypeIndex[config.TargetEntityType] = config.ID;

            XafTypesInfo.Instance.RegisterEntity(result.DtoType);
            module.AdditionalExportedTypes.Add(result.DtoType);
        }

        Tracing.Tracer.LogText($"Search DTO registered: {result.DtoType.FullName} for {config.TargetEntityType}");
        return result;
    }

    public void Unregister(int configId, ModuleBase module)
    {
        lock (_lock)
        {
            if (_entries.TryGetValue(configId, out var entry))
            {
                module.AdditionalExportedTypes.Remove(entry.DtoType);
                _entityTypeIndex.Remove(entry.TargetEntityType);
                _entries.Remove(configId);
            }
        }
    }

    public Type GetDtoType(string targetEntityTypeName)
    {
        lock (_lock)
        {
            if (_entityTypeIndex.TryGetValue(targetEntityTypeName, out var configId)
                && _entries.TryGetValue(configId, out var entry))
            {
                return entry.DtoType;
            }
            return null;
        }
    }

    public string GetSource(int configId)
    {
        lock (_lock)
        {
            return _entries.TryGetValue(configId, out var entry) ? entry.Source : null;
        }
    }


    public bool HasSearchPanel(string targetEntityTypeName)
    {
        lock (_lock)
        {
            return _entityTypeIndex.ContainsKey(targetEntityTypeName);
        }
    }

    public IReadOnlyList<RegistryEntry> GetAll()
    {
        lock (_lock)
        {
            return _entries.Values.ToList();
        }
    }
}

public class RegistryEntry
{
    public int ConfigurationId { get; set; }
    public Type DtoType { get; set; }
    public string Source { get; set; }
    public string TargetEntityType { get; set; }
}
