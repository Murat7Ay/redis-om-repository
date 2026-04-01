using Redis.OM.Modeling;

namespace CrudApp.Entity;

public abstract class BaseEntity<T> : IEntity<T>, IAuditableEntity, ISoftDeletable, IVersionable
    where T : class
{
    [RedisIdField]
    [Indexed]
    public string Id { get; set; } = null!;

    public int RowVersion { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string UpdatedBy { get; set; } = string.Empty;

    [Indexed]
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }
}
