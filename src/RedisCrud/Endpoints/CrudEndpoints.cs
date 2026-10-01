using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using RedisCrud.History;
using RedisCrud.Hosting;
using RedisCrud.Persistence;

namespace RedisCrud.Endpoints;

public static class CrudEndpoints
{
    /// <summary>Redis Query Engine refuses LIMIT windows past MAXSEARCHRESULTS (default 10 000).</summary>
    public const int MaxSearchWindow = 10_000;

    /// <summary>
    /// Maps CRUD endpoints for <typeparamref name="T"/> under <paramref name="pattern"/>:
    /// <code>
    /// GET    {pattern}?offset=&amp;limit=      list (active, by creation time)
    /// GET    {pattern}/search?q=           full-text search ([Searchable] properties only)
    /// GET    {pattern}/{id}                read; ETag = RowVersion
    /// POST   {pattern}                     create → 201 + Location + ETag
    /// PUT    {pattern}/{id}                replace; requires If-Match or body rowVersion
    /// DELETE {pattern}/{id}                soft delete (or purge, see CrudOptions.SoftDelete) → 204
    /// POST   {pattern}/{id}/restore        admin
    /// POST   {pattern}/{id}/purge          admin, irreversible → 204
    /// GET    {pattern}/{id}/history        admin
    /// </code>
    /// </summary>
    public static RouteGroupBuilder MapCrud<T>(this IEndpointRouteBuilder app, string pattern, Action<CrudEndpointOptions>? configure = null)
        where T : Entity, new()
    {
        var options = new CrudEndpointOptions();
        configure?.Invoke(options);
        app.ServiceProvider.GetRequiredService<EntityRegistry>().Add<T>();

        var group = app.MapGroup(pattern).WithTags(typeof(T).Name);

        var read = group.MapGroup("");
        if (options.AllowAnonymousRead) read.AllowAnonymous();
        else Require(read, options.ReadPolicy);

        var write = Require(group.MapGroup(""), options.WritePolicy);
        var admin = Require(group.MapGroup(""), options.AdminPolicy);

        read.MapGet("", ListAsync<T>).WithName($"List{typeof(T).Name}");
        if (EntityMetadata<T>.Instance.SearchableFields.Count > 0)
            read.MapGet("search", SearchAsync<T>).WithName($"Search{typeof(T).Name}");
        read.MapGet("{id}", GetAsync<T>).WithName($"Get{typeof(T).Name}");

        write.MapPost("", CreateAsync<T>).AddEndpointFilter(ValidateBody<T>);
        write.MapPut("{id}", ReplaceAsync<T>).AddEndpointFilter(ValidateBody<T>);
        write.MapDelete("{id}", DeleteAsync<T>);

        admin.MapPost("{id}/restore", RestoreAsync<T>);
        admin.MapPost("{id}/purge", PurgeAsync<T>);
        admin.MapGet("{id}/history", HistoryAsync<T>);

        return group;
    }

    private static RouteGroupBuilder Require(RouteGroupBuilder group, string? policy) =>
        policy is null ? group.RequireAuthorization() : group.RequireAuthorization(policy);

    // ---------------------------------------------------------------- handlers

    private static async Task<Results<Ok<Page<T>>, ValidationProblem>> ListAsync<T>(
        RedisEntityStore<T> store, IOptions<CrudOptions> options, int? offset, int? limit)
        where T : Entity, new()
    {
        if (ValidatePaging(options.Value, offset, limit, out int o, out int l) is { } problem)
            return problem;
        return TypedResults.Ok(await store.ListAsync(o, l));
    }

    private static async Task<Results<Ok<Page<T>>, ValidationProblem>> SearchAsync<T>(
        RedisEntityStore<T> store, IOptions<CrudOptions> options, string q, int? offset, int? limit)
        where T : Entity, new()
    {
        if (ValidatePaging(options.Value, offset, limit, out int o, out int l) is { } problem)
            return problem;
        return TypedResults.Ok(await store.SearchAsync(q, o, l));
    }

    private static async Task<Results<Ok<T>, ProblemHttpResult>> GetAsync<T>(
        string id, RedisEntityStore<T> store, HttpContext http)
        where T : Entity, new()
    {
        var entity = await store.GetAsync(id);
        if (entity is null)
            return NotFound();
        SetETag(http, entity);
        return TypedResults.Ok(entity);
    }

    private static async Task<Results<Created<T>, ProblemHttpResult>> CreateAsync<T>(
        T entity, RedisEntityStore<T> store, HttpContext http)
        where T : Entity, new()
    {
        var result = await store.CreateAsync(entity);
        if (result.Status != WriteStatus.Ok)
            return ToProblem(result, ifMatchUsed: false);
        SetETag(http, result.Entity!);
        return TypedResults.Created($"{http.Request.Path.Value!.TrimEnd('/')}/{result.Entity!.Id}", result.Entity);
    }

    private static async Task<Results<Ok<T>, ProblemHttpResult>> ReplaceAsync<T>(
        string id, T entity, RedisEntityStore<T> store, HttpContext http)
        where T : Entity, new()
    {
        int expected;
        bool ifMatchUsed = http.Request.Headers.IfMatch.Count > 0;
        if (ifMatchUsed)
        {
            if (!TryParseIfMatch(http, out expected))
                return TypedResults.Problem(statusCode: StatusCodes.Status412PreconditionFailed,
                    detail: "If-Match must be a single strong ETag returned by this API.");
        }
        else if (entity.RowVersion > 0)
        {
            expected = entity.RowVersion;
        }
        else
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status428PreconditionRequired,
                detail: "Send If-Match with the entity's ETag, or include rowVersion in the body.");
        }

        var result = await store.ReplaceAsync(id, entity, expected);
        if (result.Status != WriteStatus.Ok)
            return ToProblem(result, ifMatchUsed);
        SetETag(http, result.Entity!);
        return TypedResults.Ok(result.Entity!);
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync<T>(
        string id, RedisEntityStore<T> store, HttpContext http)
        where T : Entity, new()
    {
        if (!TryGetOptionalIfMatch(http, out int? expected))
            return TypedResults.Problem(statusCode: StatusCodes.Status412PreconditionFailed);
        var result = await store.DeleteAsync(id, expected);
        return result.Status == WriteStatus.Ok ? TypedResults.NoContent() : ToProblem(result, expected is not null);
    }

    private static async Task<Results<Ok<T>, ProblemHttpResult>> RestoreAsync<T>(
        string id, RedisEntityStore<T> store, HttpContext http)
        where T : Entity, new()
    {
        if (!TryGetOptionalIfMatch(http, out int? expected))
            return TypedResults.Problem(statusCode: StatusCodes.Status412PreconditionFailed);
        var result = await store.RestoreAsync(id, expected);
        if (result.Status != WriteStatus.Ok)
            return ToProblem(result, expected is not null);
        SetETag(http, result.Entity!);
        return TypedResults.Ok(result.Entity!);
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> PurgeAsync<T>(
        string id, RedisEntityStore<T> store, HttpContext http)
        where T : Entity, new()
    {
        if (!TryGetOptionalIfMatch(http, out int? expected))
            return TypedResults.Problem(statusCode: StatusCodes.Status412PreconditionFailed);
        var result = await store.PurgeAsync(id, expected);
        return result.Status == WriteStatus.Ok ? TypedResults.NoContent() : ToProblem(result, expected is not null);
    }

    private static async Task<Results<Ok<IReadOnlyList<HistoryEvent>>, ProblemHttpResult>> HistoryAsync<T>(
        string id, RedisEntityStore<T> store, int? count)
        where T : Entity, new()
    {
        if (await store.GetAsync(id, includeDeleted: true) is null)
            return NotFound();
        return TypedResults.Ok(await store.GetHistoryAsync(id, Math.Clamp(count ?? 50, 1, 1000)));
    }

    // ---------------------------------------------------------------- helpers

    private static async ValueTask<object?> ValidateBody<T>(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
        where T : Entity
    {
        var entity = context.Arguments.OfType<T>().FirstOrDefault();
        if (entity is null)
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, detail: "A JSON body is required.");

        var results = new List<ValidationResult>();
        if (Validator.TryValidateObject(entity, new ValidationContext(entity), results, validateAllProperties: true))
            return await next(context);

        var errors = results
            .SelectMany(r => r.MemberNames.DefaultIfEmpty(string.Empty), (r, member) => (member, r.ErrorMessage ?? "Invalid value."))
            .GroupBy(e => e.member)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Item2).ToArray());
        return TypedResults.ValidationProblem(errors);
    }

    private static ValidationProblem? ValidatePaging(CrudOptions options, int? offset, int? limit, out int o, out int l)
    {
        o = offset ?? 0;
        l = limit ?? options.DefaultPageSize;
        var errors = new Dictionary<string, string[]>();
        if (o < 0)
            errors["offset"] = ["offset must be >= 0."];
        if (l < 1 || l > options.MaxPageSize)
            errors["limit"] = [$"limit must be between 1 and {options.MaxPageSize}."];
        if (errors.Count == 0 && o + l > MaxSearchWindow)
            errors["offset"] = [$"offset + limit must not exceed {MaxSearchWindow}; narrow the query instead of paging deeper."];
        return errors.Count > 0 ? TypedResults.ValidationProblem(errors) : null;
    }

    private static void SetETag(HttpContext http, Entity entity) =>
        http.Response.Headers.ETag = new EntityTagHeaderValue($"\"{entity.RowVersion}\"").ToString();

    private static bool TryGetOptionalIfMatch(HttpContext http, out int? expected)
    {
        expected = null;
        if (http.Request.Headers.IfMatch.Count == 0)
            return true;
        if (!TryParseIfMatch(http, out int value))
            return false;
        expected = value;
        return true;
    }

    private static bool TryParseIfMatch(HttpContext http, out int version)
    {
        version = 0;
        return EntityTagHeaderValue.TryParseList(http.Request.Headers.IfMatch, out var tags)
               && tags is [{ IsWeak: false } tag]
               && int.TryParse(tag.Tag.AsSpan().Trim('"'), out version);
    }

    private static ProblemHttpResult NotFound() =>
        TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, detail: "Entity not found.");

    private static ProblemHttpResult ToProblem<T>(WriteResult<T> result, bool ifMatchUsed) where T : class =>
        result.Status switch
        {
            WriteStatus.NotFound => NotFound(),
            WriteStatus.VersionConflict => TypedResults.Problem(
                statusCode: ifMatchUsed ? StatusCodes.Status412PreconditionFailed : StatusCodes.Status409Conflict,
                detail: "The entity was modified by someone else. Fetch it again and retry.",
                extensions: new Dictionary<string, object?> { ["currentVersion"] = result.CurrentVersion }),
            WriteStatus.InvalidState => TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, detail: result.Detail),
            _ => TypedResults.Problem(statusCode: StatusCodes.Status500InternalServerError)
        };
}
