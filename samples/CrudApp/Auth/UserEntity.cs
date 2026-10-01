using Redis.OM.Modeling;
using RedisCrud;

namespace CrudApp.Auth;

/// <summary>
/// Stored through <see cref="RedisCrud.Persistence.RedisEntityStore{T}"/> (audit, versioning, history)
/// but deliberately not exposed through MapCrud: its HTTP contract is the DTOs in <see cref="AuthEndpoints"/>.
/// </summary>
[Document(StorageType = StorageType.Json, Prefixes = ["User"])]
public class UserEntity : Entity
{
    [Indexed]
    public string Name { get; set; } = string.Empty;

    /// <summary>ASP.NET Core Identity PBKDF2 hash (salted, versioned). Never returned, masked in history.</summary>
    [SensitiveProperty]
    public string? PasswordHash { get; set; }

    [Indexed]
    public string Role { get; set; } = Roles.Reader;
}
