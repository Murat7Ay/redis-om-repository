using CrudApp.Entity;

namespace CrudApp.Specification;

public sealed class UserByCredentialsSpec : Specification<UserEntity>
{
    public UserByCredentialsSpec(string username, string passwordHash)
        : base(user => user.Name == username && user.Password == passwordHash)
    {
    }
}
