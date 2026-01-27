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
            Subject = new ClaimsIdentity(new Claim[]
            {
                new(ClaimTypes.Name, user.Name),
                new(ClaimTypes.Role, user.Role),
                new(ClaimTypes.Hash, Guid.NewGuid().ToString())
            }),
            Expires = DateTime.UtcNow.AddMinutes(300),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);

        return tokenHandler.WriteToken(token);
    }

    internal string GetPasswordHash(string password)
    {
        ASCIIEncoding encoding = new ASCIIEncoding();

        Byte[] textBytes = encoding.GetBytes(password);
        Byte[] hashBytes;

        using (HMACSHA256 hash = new HMACSHA256(_apiSettings.GetPasswordBytes()))
            hashBytes = hash.ComputeHash(textBytes);

        return BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
    }
}