using System.Reflection;
using CrudApp.Entity;
using CrudApp.Repository;
using CrudApp.Service;
using CrudApp.Specification;

namespace CrudApp;

public static class RegisterEntities
{
    public static void AddRepositories(WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton(typeof(IRepository<>), typeof(Repository<>));
    }
    public static void AddApis(WebApplication app)
    {
        app.MapEntityApis();
        app.UserApis();
    }

    private static void MapEntityApis(this WebApplication app)
    {
        IEnumerable<Type> entityTypes = Assembly.GetExecutingAssembly()
            .GetTypes()
            .Where(type =>
                type.IsClass &&
                !type.IsAbstract &&
                type != typeof(UserEntity) &&
                type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEntity<>)));

        foreach (Type entityType in entityTypes)
        {
            MethodInfo? mapMethod = typeof(RegisterEntities)
                .GetMethod(nameof(MapEntityApiFor), BindingFlags.NonPublic | BindingFlags.Static);

            MethodInfo? genericMethod = mapMethod?.MakeGenericMethod(entityType);
            genericMethod?.Invoke(null, new object[] { app });
        }
    }

    private static void MapEntityApiFor<T>(WebApplication app) where T : class, IEntity<T>, new()
    {
        string routeBase = "/" + GetRouteName(typeof(T));
        ApiPolicyAttribute? policy = typeof(T).GetCustomAttribute<ApiPolicyAttribute>();

        ApplyPolicy(app.MapPut(routeBase, (IRepository<T> repository, T entity) => repository.AddAsync(entity)), policy);
        ApplyPolicy(app.MapPost(routeBase, (IRepository<T> repository, T entity) => repository.UpdateAsync(entity)), policy);
        ApplyPolicy(app.MapDelete(routeBase, (IRepository<T> repository, string id) => repository.DeleteAsync(id)), policy);
        ApplyPolicy(app.MapGet(routeBase, (IRepository<T> repository) => repository.ListAsync()), policy);
        ApplyPolicy(app.MapGet($"{routeBase}/{{id}}/history",
            (IRepository<T> repository, string id) => repository.GetHistoryAsync(id)), policy);
        ApplyPolicy(app.MapGet($"{routeBase}/{{id}}",
            (IRepository<T> repository, string id) => repository.FindByIdAsync(id)), policy);
        ApplyPolicy(app.MapGet($"{routeBase}/{{offset}}/{{limit}}",
            (IRepository<T> repository, int offset, int limit) => repository.PageAsync(offset, limit)), policy);
    }

    private static RouteHandlerBuilder ApplyPolicy(RouteHandlerBuilder builder, ApiPolicyAttribute? policy)
    {
        if (policy == null)
        {
            return builder;
        }

        if (string.IsNullOrWhiteSpace(policy.Policy))
        {
            return builder.RequireAuthorization();
        }

        return builder.RequireAuthorization(policy.Policy);
    }

    private static string GetRouteName(Type entityType)
    {
        string name = entityType.Name;
        if (name.EndsWith("Entity", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^"Entity".Length];
        }

        return name.ToLowerInvariant();
    }

    private static void UserApis(this WebApplication app)
    {
        app.MapPut("/user", (IRepository<UserEntity> repository, TokenService service, UserEntity entity) =>
        {
            entity.Password = service.GetPasswordHash(entity.Password);
            return repository.AddAsync(entity);
        });
        app.MapPost("/login", async (TokenService service, IRepository<UserEntity> userRepository, User userModel) =>
        {
            var spec = new UserByCredentialsSpec(userModel.Username, service.GetPasswordHash(userModel.Password));
            var result = await userRepository.FindOneAsync(spec);
            if (result.Data is null)
            {
                return new Models.Result<Models.TokenResponse>()
                    .SetReturnType(Enums.ReturnType.NotFound)
                    .SetDescription("Invalid username or password");
            }

            var token = service.GenerateToken(result.Data);
            result.Data.Password = string.Empty;
            return new Models.Result<Models.TokenResponse>()
                .SetReturnType(Enums.ReturnType.Success)
                .SetData(new Models.TokenResponse(token));
        });
    }
}
