using System.Text;

namespace CrudApp.Auth;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>The key shipped in appsettings.Development.json. Refused in every other environment.</summary>
    public const string DevelopmentSigningKey = "DEVELOPMENT-ONLY-signing-key-do-not-use-in-production-0000";

    /// <summary>HMAC-SHA256 signing key, at least 32 bytes. Supply via user-secrets or environment (Auth__SigningKey).</summary>
    public string SigningKey { get; set; } = string.Empty;

    public string Issuer { get; set; } = "crudapp";

    public string Audience { get; set; } = "crudapp";

    public int TokenLifetimeMinutes { get; set; } = 60;

    /// <summary>Requests per minute per client IP to /auth/* (brute-force throttle).</summary>
    public int AuthRequestsPerMinute { get; set; } = 10;

    /// <summary>Optional root account created at startup if no user with that name exists.</summary>
    public BootstrapAdminOptions? BootstrapAdmin { get; set; }

    public byte[] SigningKeyBytes => Encoding.UTF8.GetBytes(SigningKey);
}

public sealed class BootstrapAdminOptions
{
    public string Name { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
