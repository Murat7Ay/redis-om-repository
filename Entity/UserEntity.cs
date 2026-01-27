using Redis.OM.Modeling;

namespace CrudApp.Entity;

[Document(StorageType = StorageType.Json, Prefixes = new[] { "User" })]
public class UserEntity : IEntity<UserEntity> , IVersionAbleEntity
{
    [RedisIdField] [Indexed] public string Id { get; set; } = string.Empty;
    public IList<KeyValuePair<string, string>> GetChanges(UserEntity oldOne)
    {
        List<KeyValuePair<string, string>> changes = new List<KeyValuePair<string, string>>();
        if (!string.Equals(Name, oldOne.Name, StringComparison.Ordinal))
        {
            changes.Add(new KeyValuePair<string, string>(nameof(Name), oldOne.Name));
        }

        if (!string.Equals(Password, oldOne.Password, StringComparison.Ordinal))
        {
            changes.Add(new KeyValuePair<string, string>(nameof(Password), "***"));
        }

        if (!string.Equals(Role, oldOne.Role, StringComparison.Ordinal))
        {
            changes.Add(new KeyValuePair<string, string>(nameof(Role), oldOne.Role));
        }

        if (CreatedDate != oldOne.CreatedDate)
        {
            changes.Add(new KeyValuePair<string, string>(nameof(CreatedDate), oldOne.CreatedDate.ToString("O")));
        }

        if (LastLoginDate != oldOne.LastLoginDate)
        {
            changes.Add(new KeyValuePair<string, string>(nameof(LastLoginDate), oldOne.LastLoginDate.ToString("O")));
        }

        return changes;
    }
    [Indexed(CaseSensitive = false)] public string Name { get; set; } = string.Empty;
    [Indexed] public string Password { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public DateTime CreatedDate { get; set; }
    public DateTime LastLoginDate { get; set; }
    public int Version { get; set; }
}

public class User : IDto<UserEntity>
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}