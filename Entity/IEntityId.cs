using Redis.OM.Modeling;

namespace CrudApp.Entity;

public interface IEntityId
{
    [RedisIdField] [Indexed] string Id { get; set; }
}