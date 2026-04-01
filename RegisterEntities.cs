using System.Reflection;
using CrudApp.Entity;
using CrudApp.Extensions;
using CrudApp.Models;
using CrudApp.Repository;
using CrudApp.Service;
using CrudApp.Specification;

namespace CrudApp;

public static class RegisterEntities
{
    public static void AddRepositories(WebApplicationBuilder builder)
    {
        builder.Services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
    }

    public static void AddApis(WebApplication app)
    {
        app.MapEntityApis();
        app.MapUserApis();
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
            genericMethod?.Invoke(null, [app]);
        }
    }

    private static void MapEntityApiFor<T>(WebApplication app) where T : class, IEntity<T>, new()
    {
        string routeBase = "/" + GetRouteName(typeof(T));
        ApiPolicyAttribute? policy = typeof(T).GetCustomAttribute<ApiPolicyAttribute>();

        ApplyPolicy(app.MapPut(routeBase, async (IRepository<T> repository, T entity) =>
            (await repository.AddAsync(entity)).ToHttpResult()), policy);

        ApplyPolicy(app.MapPost(routeBase, async (IRepository<T> repository, T entity) =>
            (await repository.UpdateAsync(entity)).ToHttpResult()), policy);

        ApplyPolicy(app.MapDelete(routeBase, async (IRepository<T> repository, string id) =>
            (await repository.DeleteAsync(id)).ToHttpResult()), policy);

        ApplyPolicy(app.MapDelete($"{routeBase}/purge", async (IRepository<T> repository, string id) =>
            (await repository.PurgeAsync(id)).ToHttpResult()), policy);

        ApplyPolicy(app.MapPost($"{routeBase}/{{id}}/restore", async (IRepository<T> repository, string id) =>
            (await repository.RestoreAsync(id)).ToHttpResult()), policy);

        ApplyPolicy(app.MapGet(routeBase, async (IRepository<T> repository) =>
            (await repository.ListAsync()).ToHttpResult()), policy);

        ApplyPolicy(app.MapGet($"{routeBase}/{{id}}/history", async (IRepository<T> repository, string id) =>
            (await repository.GetHistoryAsync(id)).ToHttpResult()), policy);

        ApplyPolicy(app.MapGet($"{routeBase}/{{id}}", async (IRepository<T> repository, string id) =>
            (await repository.FindByIdAsync(id)).ToHttpResult()), policy);

        ApplyPolicy(app.MapGet($"{routeBase}/{{offset}}/{{limit}}", async (IRepository<T> repository, int offset, int limit) =>
            (await repository.PageAsync(offset, limit)).ToHttpResult()), policy);

        ApplyPolicy(app.MapGet($"{routeBase}/search", async (IRepository<T> repository, string q, int offset = 0, int limit = 20) =>
        {
            var spec = new SearchSpecification<T>(q);
            return (await repository.PageAsync(spec, offset, limit)).ToHttpResult();
        }), policy);
    }

    private static RouteHandlerBuilder ApplyPolicy(RouteHandlerBuilder builder, ApiPolicyAttribute? policy)
    {
        if (policy == null)
            return builder;

        if (string.IsNullOrWhiteSpace(policy.Policy))
            return builder.RequireAuthorization();

        return builder.RequireAuthorization(policy.Policy);
    }

    private static string GetRouteName(Type entityType)
    {
        string name = entityType.Name;
        if (name.EndsWith("Entity", StringComparison.OrdinalIgnoreCase))
            name = name[..^"Entity".Length];

        return name.ToLowerInvariant();
    }

    private static void MapUserApis(this WebApplication app)
    {
        app.MapPut("/user", async (IRepository<UserEntity> repository, TokenService service, UserEntity entity) =>
        {
            entity.Password = service.GetPasswordHash(entity.Password);
            var result = await repository.AddAsync(entity);
            return result.ToHttpResult();
        });

        app.MapPost("/login", async (TokenService service, IRepository<UserEntity> userRepository, UserLoginRequest request) =>
        {
            var spec = new UserByCredentialsSpec(request.Username, service.GetPasswordHash(request.Password));
            var result = await userRepository.FindOneAsync(spec);
            if (result.Data is null)
            {
                return new Result<TokenResponse>()
                    .SetReturnType(Enums.ReturnType.NotFound)
                    .SetDescription("Invalid username or password")
                    .ToHttpResult();
            }

            var token = service.GenerateToken(result.Data);
            result.Data.Password = string.Empty;
            return new Result<TokenResponse>()
                .SetReturnType(Enums.ReturnType.Success)
                .SetData(new TokenResponse(token))
                .ToHttpResult();
        });
    }
}
