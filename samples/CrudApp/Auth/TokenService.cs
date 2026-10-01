using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace CrudApp.Auth;

public sealed class TokenService(IOptions<AuthOptions> options, TimeProvider time)
{
    public (string Token, DateTimeOffset ExpiresAt) Issue(UserEntity user)
    {
        var settings = options.Value;
        var now = time.GetUtcNow();
        var expires = now.AddMinutes(settings.TokenLifetimeMinutes);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = settings.Issuer,
            Audience = settings.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            Subject = new ClaimsIdentity(
            [
                new Claim("sub", user.Id),
                new Claim("name", user.Name),
                new Claim("role", user.Role),
                new Claim("jti", Guid.NewGuid().ToString("N"))
            ]),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(settings.SigningKeyBytes), SecurityAlgorithms.HmacSha256)
        };

        return (new JsonWebTokenHandler().CreateToken(descriptor), expires);
    }
}
