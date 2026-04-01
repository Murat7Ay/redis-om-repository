using System.Reflection;
using Redis.OM.Modeling;

namespace CrudApp.ChangeTracking;

/// <summary>
/// Detects property-level changes between two entity instances using reflection.
/// Skips Id, RowVersion, audit columns, soft-delete fields, and non-string collections.
/// Masks properties decorated with <see cref="SensitivePropertyAttribute"/>.
/// </summary>
public class ReflectionChangeTracker<T> : IChangeTracker<T> where T : class
{
    private static readonly HashSet<string> SkippedProperties =
    [
        "Id", "RowVersion", "Version",
        "CreatedAt", "UpdatedAt", "CreatedBy", "UpdatedBy",
        "IsDeleted", "DeletedAt", "DeletedBy"
    ];

    private static readonly PropertyInfo[] TrackedProperties = BuildTrackedProperties();

    private static PropertyInfo[] BuildTrackedProperties()
    {
        return typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p =>
                p.CanRead
                && !SkippedProperties.Contains(p.Name)
                && p.GetCustomAttribute<RedisIdFieldAttribute>() == null
                && (p.PropertyType == typeof(string)
                    || !typeof(System.Collections.IEnumerable).IsAssignableFrom(p.PropertyType)))
            .ToArray();
    }

    public IList<KeyValuePair<string, string>> DetectChanges(T current, T previous)
    {
        var changes = new List<KeyValuePair<string, string>>();

        foreach (var prop in TrackedProperties)
        {
            var currentValue = prop.GetValue(current);
            var previousValue = prop.GetValue(previous);

            if (Equals(currentValue, previousValue))
                continue;

            bool isSensitive = prop.GetCustomAttribute<SensitivePropertyAttribute>() != null;
            string recordedValue = isSensitive ? "***" : (previousValue?.ToString() ?? string.Empty);
            changes.Add(new KeyValuePair<string, string>(prop.Name, recordedValue));
        }

        return changes;
    }
}
