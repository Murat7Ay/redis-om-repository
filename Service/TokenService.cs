using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using CrudApp.Entity;
using CrudApp.Settings;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CrudApp.Service;

internal class TokenService
{
    private readonly ApiSettings _apiSettings;

    public TokenService(IOptions<ApiSettings> apiOptions)
    {
        _apiSettings = apiOptions.Value;
    }

    internal string GenerateToken(UserEntity user)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = _apiSettings.GetSecretBytes();

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Name, user.Name),
                new Claim(ClaimTypes.Role, user.Role),
                new Claim(ClaimTypes.Hash, Guid.NewGuid().ToString())
            ]),
            Expires = DateTime.UtcNow.AddMinutes(_apiSettings.TokenExpiryMinutes),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(key),
                SecurityAlgorithms.HmacSha256Signature)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    internal string GetPasswordHash(string password)
    {
        byte[] textBytes = Encoding.UTF8.GetBytes(password);
        using var hmac = new HMACSHA256(_apiSettings.GetPasswordBytes());
        byte[] hashBytes = hmac.ComputeHash(textBytes);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
