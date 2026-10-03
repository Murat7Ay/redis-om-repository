using System.Reflection;
using Redis.OM.Modeling;

namespace RedisCrud;

/// <summary>
/// Everything the framework needs to know about an entity type, computed once per type.
/// This is the only place that reflects over entity attributes.
/// </summary>
internal sealed class EntityMetadata<T> where T : Entity
{
    public static EntityMetadata<T> Instance { get; } = new();

    private EntityMetadata()
    {
        var type = typeof(T);
        var document = type.GetCustomAttribute<DocumentAttribute>()
            ?? throw new InvalidOperationException($"{type.Name} must be decorated with [Document(StorageType = StorageType.Json, ...)].");

        if (document.StorageType != StorageType.Json)
            throw new InvalidOperationException($"{type.Name}: only StorageType.Json is supported.");

        KeyPrefix = document.Prefixes is { Length: > 0 } prefixes ? prefixes[0] : type.FullName!;
        IndexName = string.IsNullOrEmpty(document.IndexName) ? $"{type.Name.ToLowerInvariant()}-idx" : document.IndexName;

        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        SearchableFields = properties
            .Where(p => p.PropertyType == typeof(string) && p.GetCustomAttribute<SearchableAttribute>() is not null)
            .Select(p => p.Name)
            .ToArray();

        SensitiveProperties = properties
            .Where(p => p.GetCustomAttribute<SensitivePropertyAttribute>() is not null)
            .ToArray();

        SensitiveNames = SensitiveProperties.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Redis key prefix from <c>[Document(Prefixes = ...)]</c>; documents live at <c>{KeyPrefix}:{Id}</c>.</summary>
    public string KeyPrefix { get; }

    /// <summary>Redis Query Engine index name, following the Redis OM convention.</summary>
    public string IndexName { get; }

    /// <summary>String properties marked <c>[Searchable]</c> (indexed as TEXT).</summary>
    public IReadOnlyList<string> SearchableFields { get; }

    public IReadOnlyList<PropertyInfo> SensitiveProperties { get; }

    public IReadOnlySet<string> SensitiveNames { get; }

    public string DocumentKey(string id) => $"{KeyPrefix}:{id}";

    /// <summary>History lives outside the index prefix so the Query Engine never scans it.</summary>
    public string HistoryKey(string id) => $"history:{KeyPrefix}:{id}";
}
