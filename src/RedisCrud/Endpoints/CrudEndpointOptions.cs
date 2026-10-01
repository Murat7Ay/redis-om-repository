namespace RedisCrud.Endpoints;

/// <summary>
/// Authorization per operation class. A <c>null</c> policy means "any authenticated user":
/// nothing is anonymous unless <see cref="AllowAnonymousRead"/> is set explicitly.
/// </summary>
public sealed class CrudEndpointOptions
{
    /// <summary>GET list, GET by id, search.</summary>
    public string? ReadPolicy { get; set; }

    /// <summary>POST create, PUT replace, DELETE.</summary>
    public string? WritePolicy { get; set; }

    /// <summary>Restore, purge, history. History exposes past values and actors, so it is not a plain read.</summary>
    public string? AdminPolicy { get; set; }

    /// <summary>Opt-in: make read endpoints anonymous.</summary>
    public bool AllowAnonymousRead { get; set; }
}
