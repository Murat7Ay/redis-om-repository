using Microsoft.AspNetCore.Authorization;

namespace CrudApp.Auth;

/// <summary>Hierarchical roles: root ⊇ moderator ⊇ reader. Policy names equal role names.</summary>
public static class Roles
{
    public const string Reader = "reader";
    public const string Moderator = "moderator";
    public const string Root = "root";

    public static readonly string[] All = [Reader, Moderator, Root];

    public static void AddPolicies(AuthorizationOptions options)
    {
        options.AddPolicy(Reader, p => p.RequireRole(Reader, Moderator, Root));
        options.AddPolicy(Moderator, p => p.RequireRole(Moderator, Root));
        options.AddPolicy(Root, p => p.RequireRole(Root));
    }
}
