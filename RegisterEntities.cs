using CrudApp.Entity;
using CrudApp.Repository;
using CrudApp.Service;

namespace CrudApp;

public static class RegisterEntities
{
    public static void AddRepositories(WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton(typeof(IRepository<>), typeof(Repository<>));
    }
    public static void AddApis(WebApplication app)
    {
        app.RoseApis();
        app.UserApis();
    }

    private static void RoseApis(this WebApplication app)
    {
        app.MapPut("/rose", (IRepository<RoseEntity> repository, RoseEntity entity) => repository.Save(entity))
            .RequireAuthorization("root");
        app.MapPost("/rose", (IRepository<RoseEntity> repository, RoseEntity entity) => repository.Update(entity))
            .RequireAuthorization("root");
        app.MapDelete("/rose", (IRepository<RoseEntity> repository, string id) => repository.Delete(id))
            .RequireAuthorization("root");
        app.MapGet("/rose", (IRepository<RoseEntity> repository) => repository.Get()).RequireAuthorization("root");
        app.MapGet("/rose/{id}/history", (IRepository<RoseEntity> repository, string id) => repository.GetHistory(id))
            .RequireAuthorization("root");
        app.MapGet("/rose/{id}", (IRepository<RoseEntity> repository, string id) => repository.FindById(id))
            .RequireAuthorization("root");
        ;
        app.MapGet("/rose/{offset}/{limit}",
                (IRepository<RoseEntity> repository, int offset, int limit) => repository.Get(offset, limit))
            .RequireAuthorization("root");
        ;
    }

    private static void UserApis(this WebApplication app)
    {
        app.MapPut("/user", (IRepository<UserEntity> repository, TokenService service, UserEntity entity) =>
        {
            entity.Password = service.GetPasswordHash(entity.Password);
            return repository.Save(entity);
        });
        app.MapPost("/login", (TokenService service, IRepository<UserEntity> userRepository, User userModel) =>
        {
            var userEntities = userRepository.Get(x =>
                x.Name == userModel.Username && x.Password == service.GetPasswordHash(userModel.Password)).Data;
            if (userEntities != null)
            {
                var user = userEntities.FirstOrDefault();

                if (user is null)
                    return Results.NotFound(new { message = "Invalid username or password" });
                var token = service.GenerateToken(user);

                user.Password = string.Empty;

                return Results.Ok(new { token = token });
            }

            return Results.NotFound(new { message = "Invalid username or password" });
        });
    }
}
