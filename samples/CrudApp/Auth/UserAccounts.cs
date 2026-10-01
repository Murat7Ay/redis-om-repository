using Microsoft.AspNetCore.Identity;
using RedisCrud.Persistence;
using StackExchange.Redis;

namespace CrudApp.Auth;

/// <summary>
/// User registration and credential checks. Usernames are unique via a Redis-native reservation
/// key (<c>SET NX</c>), not via a query, so uniqueness holds under concurrent registrations.
/// </summary>
public sealed class UserAccounts(RedisEntityStore<UserEntity> store, IConnectionMultiplexer redis)
{
    private static readonly PasswordHasher<UserEntity> Hasher = new();

    // Verified against when the user does not exist, so both paths cost one PBKDF2 run.
    private static readonly string DummyHash = Hasher.HashPassword(new UserEntity(), Guid.NewGuid().ToString());

    private IDatabase Db => redis.GetDatabase();

    private static RedisKey NameKey(string name) => $"user-name:{name.Trim().ToUpperInvariant()}";

    public async Task<UserEntity?> RegisterAsync(string name, string password, string role)
    {
        var key = NameKey(name);
        if (!await Db.StringSetAsync(key, "pending", when: When.NotExists))
            return null;

        try
        {
            var user = new UserEntity { Name = name.Trim(), Role = role };
            user.PasswordHash = Hasher.HashPassword(user, password);
            var result = await store.CreateAsync(user);
            await Db.StringSetAsync(key, result.Entity!.Id);
            return result.Entity;
        }
        catch
        {
            await Db.KeyDeleteAsync(key);
            throw;
        }
    }

    public async Task<UserEntity?> FindByNameAsync(string name)
    {
        string? id = await Db.StringGetAsync(NameKey(name));
        return id is null or "pending" ? null : await store.GetAsync(id);
    }

    public async Task<UserEntity?> VerifyAsync(string name, string password)
    {
        var user = await FindByNameAsync(name);
        var outcome = Hasher.VerifyHashedPassword(user ?? new UserEntity(), user?.PasswordHash ?? DummyHash, password);
        return user is not null && outcome != PasswordVerificationResult.Failed ? user : null;
    }
}
