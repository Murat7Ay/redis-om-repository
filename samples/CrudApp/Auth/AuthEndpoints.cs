using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http.HttpResults;
using RedisCrud.Persistence;

namespace CrudApp.Auth;

public sealed record RegisterRequest(
    [Required, RegularExpression("^[A-Za-z0-9._-]{3,32}$")] string Name,
    [Required, StringLength(128, MinimumLength = 8)] string Password);

public sealed record LoginRequest([Required] string Name, [Required] string Password);

public sealed record TokenResponse(string AccessToken, string TokenType, DateTimeOffset ExpiresAt);

public sealed record ChangeRoleRequest([Required] string Role);

public sealed record UserResponse(string Id, string Name, string Role, DateTime CreatedAt)
{
    public static UserResponse From(UserEntity u) => new(u.Id, u.Name, u.Role, u.CreatedAt);
}

public static class AuthEndpoints
{
    public const string RateLimitPolicy = "auth";

    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/auth").WithTags("Auth").RequireRateLimiting(RateLimitPolicy);

        // Open registration always yields the lowest role; elevation is a root-only operation below.
        auth.MapPost("/register", async Task<Results<Created<UserResponse>, ProblemHttpResult>> (
            RegisterRequest request, UserAccounts accounts) =>
        {
            var user = await accounts.RegisterAsync(request.Name, request.Password, Roles.Reader);
            return user is null
                ? TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, detail: "The name is already taken.")
                : TypedResults.Created($"/users/{user.Id}", UserResponse.From(user));
        }).AllowAnonymous();

        auth.MapPost("/login", async Task<Results<Ok<TokenResponse>, ProblemHttpResult>> (
            LoginRequest request, UserAccounts accounts, TokenService tokens) =>
        {
            var user = await accounts.VerifyAsync(request.Name, request.Password);
            if (user is null)
                return TypedResults.Problem(statusCode: StatusCodes.Status401Unauthorized, detail: "Invalid name or password.");

            var (token, expiresAt) = tokens.Issue(user);
            return TypedResults.Ok(new TokenResponse(token, "Bearer", expiresAt));
        }).AllowAnonymous();

        var users = app.MapGroup("/users").WithTags("Users").RequireAuthorization(Roles.Root);

        users.MapGet("/", async (RedisEntityStore<UserEntity> store, int? offset, int? limit) =>
        {
            var page = await store.ListAsync(Math.Max(offset ?? 0, 0), Math.Clamp(limit ?? 20, 1, 100));
            return TypedResults.Ok(new Page<UserResponse>(page.Items.Select(UserResponse.From).ToList(), page.Offset, page.Limit, page.Total));
        });

        users.MapPut("/{id}/role", async Task<Results<Ok<UserResponse>, ValidationProblem, ProblemHttpResult>> (
            string id, ChangeRoleRequest request, RedisEntityStore<UserEntity> store) =>
        {
            if (!Roles.All.Contains(request.Role))
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["role"] = [$"Must be one of: {string.Join(", ", Roles.All)}."] });

            var user = await store.GetAsync(id);
            if (user is null)
                return TypedResults.Problem(statusCode: StatusCodes.Status404NotFound);

            user.Role = request.Role;
            var result = await store.ReplaceAsync(id, user, user.RowVersion);
            return result.Status == WriteStatus.Ok
                ? TypedResults.Ok(UserResponse.From(result.Entity!))
                : TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, detail: "The user changed concurrently; retry.");
        });
    }
}
