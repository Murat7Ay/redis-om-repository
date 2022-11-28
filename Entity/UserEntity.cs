using Redis.OM.Modeling;

namespace CrudApp.Entity;

[Document(StorageType = StorageType.Json, Prefixes = new[] { "User" })]
public class UserEntity : IEntity<UserEntity> , IVersionAbleEntity
{
    [RedisIdField] [Indexed] public string Id { get; set; }
    public IList<KeyValuePair<string, string>> GetChanges(UserEntity newOne)
    {
        throw new NotImplementedException();
    }
    [Indexed(CaseSensitive = false)] public string Name { get; set; }
    [Indexed] public string Password { get; set; }
    public string Role { get; set; }
    public DateTime CreatedDate { get; set; }
    public DateTime LastLoginDate { get; set; }
    public int Version { get; set; }
}

public class User : IDto<UserEntity>
{
    public string Username { get; set; }
    public string Password { get; set; }
}