using CrudApp.ChangeTracking;
using Redis.OM.Modeling;

namespace CrudApp.Entity;

[Document(StorageType = StorageType.Json, Prefixes = new[] { "User" })]
public class UserEntity : BaseEntity<UserEntity>
{
    [Indexed(CaseSensitive = false)]
    public string Name { get; set; } = string.Empty;

    [SensitiveProperty]
    [Indexed]
    public string Password { get; set; } = string.Empty;

    public string Role { get; set; } = string.Empty;

    public DateTime LastLoginDate { get; set; }
}
