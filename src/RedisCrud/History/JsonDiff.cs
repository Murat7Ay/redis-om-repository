using System.Text.Json;
using System.Text.Json.Nodes;

namespace RedisCrud.History;

/// <summary>
/// Structural diff between two JSON snapshots of the same entity.
/// Objects are compared member by member (paths like <c>Address.City</c>);
/// arrays and scalars are compared as whole values and recorded as such.
/// Working on the serialized form makes the diff culture-invariant and gives
/// nested objects, collections, dictionaries and null transitions a well-defined representation.
/// </summary>
internal static class JsonDiff
{
    public const string Mask = "***";

    public static List<FieldChange> Diff(
        JsonObject? before,
        JsonObject? after,
        IReadOnlySet<string> ignoredTopLevel,
        IReadOnlySet<string> sensitiveTopLevel)
    {
        var changes = new List<FieldChange>();
        var names = new List<string>();
        if (before is not null) names.AddRange(before.Select(p => p.Key));
        if (after is not null) names.AddRange(after.Select(p => p.Key).Where(k => before is null || !before.ContainsKey(k)));

        foreach (string name in names)
        {
            if (ignoredTopLevel.Contains(name))
                continue;

            JsonNode? oldValue = before?[name];
            JsonNode? newValue = after?[name];

            if (sensitiveTopLevel.Contains(name))
            {
                if (!JsonNode.DeepEquals(oldValue, newValue))
                    changes.Add(new FieldChange(name, oldValue is null ? null : JsonValue.Create(Mask), newValue is null ? null : JsonValue.Create(Mask)));
                continue;
            }

            DiffNode(name, oldValue, newValue, changes);
        }

        return changes;
    }

    private static void DiffNode(string path, JsonNode? oldValue, JsonNode? newValue, List<FieldChange> changes)
    {
        if (oldValue is JsonObject oldObject && newValue is JsonObject newObject)
        {
            var keys = oldObject.Select(p => p.Key).Union(newObject.Select(p => p.Key), StringComparer.Ordinal);
            foreach (string key in keys)
                DiffNode($"{path}.{key}", oldObject[key], newObject[key], changes);
            return;
        }

        if (!ValueEquals(oldValue, newValue))
            changes.Add(new FieldChange(path, oldValue?.DeepClone(), newValue?.DeepClone()));
    }

    /// <summary>Numbers compare by value (10.5 == 10.50), everything else structurally.</summary>
    private static bool ValueEquals(JsonNode? a, JsonNode? b)
    {
        if (a is JsonValue va && b is JsonValue vb
            && va.GetValueKind() == JsonValueKind.Number && vb.GetValueKind() == JsonValueKind.Number
            && va.TryGetValue(out decimal da) && vb.TryGetValue(out decimal db))
            return da == db;

        return JsonNode.DeepEquals(a, b);
    }
}

/// <param name="Path">Property path; nested object members are dot-separated.</param>
/// <param name="Old">Value before the write (<c>null</c> = absent or JSON null).</param>
/// <param name="New">Value after the write.</param>
public sealed record FieldChange(string Path, JsonNode? Old, JsonNode? New);
