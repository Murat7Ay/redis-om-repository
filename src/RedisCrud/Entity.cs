using Redis.OM.Modeling;

namespace RedisCrud;

/// <summary>
/// Base type for documents managed by <see cref="Persistence.RedisEntityStore{T}"/>.
/// Every property declared here is <b>server-owned</b>: values sent by clients are ignored
/// on create and replace, and the store is the only writer.
/// </summary>
/// <remarks>
/// Derived types must carry <c>[Document(StorageType = StorageType.Json, Prefixes = new[] { "..." })]</c>.
/// Every public property declared on the derived type is client-writable through the generic endpoints.
/// If an entity has fields clients must not set, do not expose it through <c>MapCrud</c>; write a handler.
/// </remarks>
public abstract class Entity
{
    [RedisIdField]
    [Indexed]
    public string Id { get; set; } = string.Empty;

    /// <summary>Optimistic concurrency token. Incremented atomically by every write; exposed as the HTTP ETag.</summary>
    public int RowVersion { get; set; }

    [Indexed(Sortable = true)]
    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public string UpdatedBy { get; set; } = string.Empty;

    [Indexed]
    public bool IsDeleted { get; set; }

    public DateTime? DeletedAt { get; set; }

    public string? DeletedBy { get; set; }

    internal static readonly HashSet<string> SystemProperties = new(StringComparer.Ordinal)
    {
        nameof(Id), nameof(RowVersion), nameof(CreatedAt), nameof(UpdatedAt),
        nameof(CreatedBy), nameof(UpdatedBy), nameof(IsDeleted), nameof(DeletedAt), nameof(DeletedBy)
    };

    internal void CopySystemFieldsFrom(Entity source)
    {
        Id = source.Id;
        RowVersion = source.RowVersion;
        CreatedAt = source.CreatedAt;
        UpdatedAt = source.UpdatedAt;
        CreatedBy = source.CreatedBy;
        UpdatedBy = source.UpdatedBy;
        IsDeleted = source.IsDeleted;
        DeletedAt = source.DeletedAt;
        DeletedBy = source.DeletedBy;
    }
}

/// <summary>
/// Marks a property as sensitive: it is never written to HTTP responses (write-only),
/// its values are masked in history, and on replace a null value keeps the stored value.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class SensitivePropertyAttribute : Attribute;
