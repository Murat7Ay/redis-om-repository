using System.Net.Http.Headers;
using System.Net.Http.Json;
using CrudApp.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace RedisCrud.Tests.Api;

public sealed class ApiFactory(RedisFixture redis, string environment = "Production", IDictionary<string, string?>? overrides = null)
    : WebApplicationFactory<Program>
{
    public const string SigningKey = "test-signing-key-that-is-long-enough-for-hmac-sha256";
    public const string RootName = "root-admin";
    public const string RootPassword = "root-password-123";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        var settings = new Dictionary<string, string?>
        {
            ["Crud:ConnectionString"] = redis.ConnectionString,
            ["Auth:SigningKey"] = SigningKey,
            ["Auth:BootstrapAdmin:Name"] = RootName,
            ["Auth:BootstrapAdmin:Password"] = RootPassword,
            ["Auth:AuthRequestsPerMinute"] = "10000"
        };
        foreach (var (key, value) in overrides ?? new Dictionary<string, string?>())
            settings[key] = value;
        foreach (var (key, value) in settings)
            builder.UseSetting(key, value);
    }

    public async Task<HttpClient> ClientForAsync(string role)
    {
        var anonymous = CreateClient();
        string name, password;
        if (role == Roles.Root)
        {
            (name, password) = (RootName, RootPassword);
        }
        else
        {
            name = $"{role}-{Guid.NewGuid():N}"[..30];
            password = "user-password-123";
            var registered = await anonymous.PostAsJsonAsync("/auth/register", new { name, password });
            registered.EnsureSuccessStatusCode();
            if (role != Roles.Reader)
            {
                var user = await registered.Content.ReadFromJsonAsync<UserResponse>();
                var root = await ClientForAsync(Roles.Root);
                (await root.PutAsJsonAsync($"/users/{user!.Id}/role", new { role })).EnsureSuccessStatusCode();
            }
        }

        var login = await anonymous.PostAsJsonAsync("/auth/login", new { name, password });
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<TokenResponse>())!.AccessToken;

        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
