using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using CrudApp.Auth;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace RedisCrud.Tests.Api;

[Collection(RedisCollection.Name)]
public class AuthApiTests(RedisFixture redis) : IAsyncLifetime
{
    private ApiFactory _factory = null!;

    public Task InitializeAsync()
    {
        _factory = new ApiFactory(redis);
        _ = _factory.Server;
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private static string UniqueName() => $"u{Guid.NewGuid():N}"[..20];

    [Fact]
    public async Task Registration_cannot_choose_a_role_and_never_returns_the_hash()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/auth/register", new { name = UniqueName(), password = "password-123", role = Roles.Root });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        string json = await response.Content.ReadAsStringAsync();
        JsonNode.Parse(json)!["role"]!.GetValue<string>().Should().Be(Roles.Reader);
        json.Should().NotContainEquivalentOf("password");
    }

    [Fact]
    public async Task Names_are_unique_case_insensitively_even_under_concurrency()
    {
        var client = _factory.CreateClient();
        string name = UniqueName();

        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(i =>
            client.PostAsJsonAsync("/auth/register", new { name = i % 2 == 0 ? name : name.ToUpperInvariant(), password = "password-123" })));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.Conflict).Should().Be(9);
    }

    [Theory]
    [InlineData("ab", "password-123")]
    [InlineData("has space", "password-123")]
    [InlineData("validname", "short")]
    public async Task Registration_validates_input(string name, string password)
    {
        var response = await _factory.CreateClient().PostAsJsonAsync("/auth/register", new { name, password });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Login_failures_are_indistinguishable()
    {
        var client = _factory.CreateClient();
        string name = UniqueName();
        await client.PostAsJsonAsync("/auth/register", new { name, password = "password-123" });

        var wrongPassword = await client.PostAsJsonAsync("/auth/login", new { name, password = "nope-nope-nope" });
        var unknownUser = await client.PostAsJsonAsync("/auth/login", new { name = UniqueName(), password = "nope-nope-nope" });

        wrongPassword.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        unknownUser.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var a = (await wrongPassword.Content.ReadFromJsonAsync<JsonObject>())!;
        var b = (await unknownUser.Content.ReadFromJsonAsync<JsonObject>())!;
        a["detail"]!.GetValue<string>().Should().Be(b["detail"]!.GetValue<string>());
    }

    [Fact]
    public async Task Stored_password_is_a_salted_pbkdf2_hash()
    {
        var client = _factory.CreateClient();
        string n1 = UniqueName(), n2 = UniqueName();
        await client.PostAsJsonAsync("/auth/register", new { name = n1, password = "same-password-1" });
        await client.PostAsJsonAsync("/auth/register", new { name = n2, password = "same-password-1" });

        using var scope = _factory.Services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<UserAccounts>();
        var u1 = (await accounts.FindByNameAsync(n1))!;
        var u2 = (await accounts.FindByNameAsync(n2))!;

        u1.PasswordHash.Should().NotBe(u2.PasswordHash, "hashes must be salted");
        Convert.FromBase64String(u1.PasswordHash!)[0].Should().Be(0x01, "ASP.NET Core Identity V3 format (PBKDF2)");
    }

    [Fact]
    public async Task Root_can_promote_users_and_the_new_role_applies_after_login()
    {
        var root = await _factory.ClientForAsync(Roles.Root);
        var anonymous = _factory.CreateClient();
        string name = UniqueName();
        var user = (await (await anonymous.PostAsJsonAsync("/auth/register", new { name, password = "password-123" }))
            .Content.ReadFromJsonAsync<UserResponse>())!;

        (await root.PutAsJsonAsync($"/users/{user.Id}/role", new { role = "god" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await root.PutAsJsonAsync($"/users/{user.Id}/role", new { role = Roles.Moderator })).StatusCode.Should().Be(HttpStatusCode.OK);

        var token = (await (await anonymous.PostAsJsonAsync("/auth/login", new { name, password = "password-123" }))
            .Content.ReadFromJsonAsync<TokenResponse>())!.AccessToken;
        var payload = Encoding.UTF8.GetString(Base64UrlDecode(token.Split('.')[1]));
        JsonNode.Parse(payload)!["role"]!.GetValue<string>().Should().Be(Roles.Moderator);
    }

    [Fact]
    public async Task Tokens_carry_issuer_audience_subject_and_bounded_lifetime()
    {
        var anonymous = _factory.CreateClient();
        var response = await anonymous.PostAsJsonAsync("/auth/login", new { name = ApiFactory.RootName, password = ApiFactory.RootPassword });
        var token = (await response.Content.ReadFromJsonAsync<TokenResponse>())!;

        var claims = JsonNode.Parse(Encoding.UTF8.GetString(Base64UrlDecode(token.AccessToken.Split('.')[1])))!;
        claims["iss"]!.GetValue<string>().Should().Be("crudapp");
        claims["aud"]!.GetValue<string>().Should().Be("crudapp");
        claims["sub"]!.GetValue<string>().Should().MatchRegex("^[0-9a-f]{32}$");
        token.ExpiresAt.Should().BeCloseTo(DateTimeOffset.UtcNow.AddMinutes(60), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task User_listing_never_exposes_password_hashes()
    {
        var root = await _factory.ClientForAsync(Roles.Root);
        string json = await root.GetStringAsync("/users");
        json.Should().NotContainEquivalentOf("password");
    }

    [Fact]
    public async Task Auth_endpoints_are_rate_limited()
    {
        await using var limited = new ApiFactory(redis, overrides: new Dictionary<string, string?> { ["Auth:AuthRequestsPerMinute"] = "3" });
        var client = limited.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (int i = 0; i < 5; i++)
            statuses.Add((await client.PostAsJsonAsync("/auth/login", new { name = "x", password = "y" })).StatusCode);

        statuses.Should().Contain(HttpStatusCode.TooManyRequests);
    }

    [Theory]
    [InlineData("")]
    [InlineData("too-short")]
    [InlineData(AuthOptions.DevelopmentSigningKey)]
    public void Startup_rejects_missing_short_or_development_signing_keys_in_production(string key)
    {
        using var factory = new ApiFactory(redis, overrides: new Dictionary<string, string?> { ["Auth:SigningKey"] = key });
        FluentActions.Invoking(() => factory.Server).Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public async Task Development_environment_exposes_openapi_and_maps_bad_json_to_400()
    {
        await using var dev = new ApiFactory(redis, environment: "Development");
        var client = dev.CreateClient();
        (await client.GetAsync("/openapi/v1.json")).StatusCode.Should().Be(HttpStatusCode.OK);

        var login = await client.PostAsync("/auth/login", new StringContent("{bad", Encoding.UTF8, "application/json"));
        login.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private static byte[] Base64UrlDecode(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }
}
