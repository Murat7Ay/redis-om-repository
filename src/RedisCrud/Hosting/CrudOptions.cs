using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace RedisCrud.Hosting;

public sealed class CrudOptions
{
    public const string SectionName = "Crud";

    /// <summary>Redis connection string. The connection is established lazily and retried; startup does not fail when Redis is down.</summary>
    public string ConnectionString { get; set; } = "localhost:6379";

    /// <summary>History events kept per entity (exact trim). 0 disables history.</summary>
    public int HistoryMaxLength { get; set; } = 100;

    /// <summary>When true, DELETE marks entities deleted; when false, DELETE purges.</summary>
    public bool SoftDelete { get; set; } = true;

    public int DefaultPageSize { get; set; } = 20;

    public int MaxPageSize { get; set; } = 100;

    /// <summary>
    /// When an index definition no longer matches the entity attributes, drop and recreate the index
    /// (documents are kept and re-indexed in the background). When false, startup fails instead.
    /// </summary>
    public bool RecreateStaleIndexes { get; set; } = true;
}

/// <summary>Who is performing a write. Recorded in audit fields and history.</summary>
public interface ICrudActor
{
    string Id { get; }
}

/// <summary>
/// Uses the stable subject identifier (<c>sub</c> / NameIdentifier), not the display name:
/// names are not guaranteed unique, ids are.
/// </summary>
public sealed class HttpContextCrudActor(IHttpContextAccessor accessor) : ICrudActor
{
    public string Id
    {
        get
        {
            var user = accessor.HttpContext?.User;
            if (user?.Identity?.IsAuthenticated != true)
                return accessor.HttpContext is null ? "system" : "anonymous";

            return user.FindFirstValue("sub")
                ?? user.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? user.Identity.Name
                ?? "unknown";
        }
    }
}
