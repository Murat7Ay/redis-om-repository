namespace RedisCrud.Persistence;

public enum WriteStatus
{
    Ok,
    NotFound,

    /// <summary>The expected version did not match the stored version.</summary>
    VersionConflict,

    /// <summary>The operation is not valid in the entity's current lifecycle state (e.g. updating a deleted entity).</summary>
    InvalidState
}

public sealed record WriteResult<T>(WriteStatus Status, T? Entity = null, int? CurrentVersion = null, string? Detail = null)
    where T : class
{
    internal static WriteResult<T> Success(T entity) => new(WriteStatus.Ok, entity);
    internal static WriteResult<T> Missing() => new(WriteStatus.NotFound);
    internal static WriteResult<T> Conflict(int currentVersion) => new(WriteStatus.VersionConflict, CurrentVersion: currentVersion);
    internal static WriteResult<T> Invalid(string detail) => new(WriteStatus.InvalidState, Detail: detail);
}

/// <param name="Items">The page.</param>
/// <param name="Offset">Zero-based offset of the first item.</param>
/// <param name="Limit">Requested page size.</param>
/// <param name="Total">Total matching documents, from the same FT.SEARCH round trip as the items.</param>
public sealed record Page<T>(IReadOnlyList<T> Items, int Offset, int Limit, long Total);
