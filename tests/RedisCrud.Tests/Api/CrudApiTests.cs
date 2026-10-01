using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CrudApp.Auth;
using CrudApp.Entities;

namespace RedisCrud.Tests.Api;

[Collection(RedisCollection.Name)]
public class CrudApiTests(RedisFixture redis) : IAsyncLifetime
{
    private ApiFactory _factory = null!;

    public async Task InitializeAsync()
    {
        await redis.ResetAsync<ProductEntity>();
        _factory = new ApiFactory(redis);
        _ = _factory.Server; // start host: provisions indexes and bootstrap root
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private static object NewProduct(string name = "Kalem", decimal price = 10.5m) =>
        new { name, description = "Kırmızı tükenmez kalemler", price, category = "ofis", stock = 3, tags = new[] { "a" } };

    private static async Task<JsonObject> BodyAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonObject>())!;

    private static async Task<(string Id, string ETag, JsonObject Body)> CreateAsync(HttpClient client, object? product = null)
    {
        var response = await client.PostAsJsonAsync("/products", product ?? NewProduct());
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await BodyAsync(response);
        return (body["id"]!.GetValue<string>(), response.Headers.ETag!.Tag, body);
    }

    // ------------------------------------------------------------ authorization

    [Fact]
    public async Task Endpoints_require_authentication_unless_explicitly_anonymous()
    {
        var anonymous = _factory.CreateClient();

        (await anonymous.GetAsync("/products")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsJsonAsync("/products", NewProduct())).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync("/roses")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await anonymous.PostAsJsonAsync("/roses", new { name = "r" })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync("/users")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Policies_are_per_operation_and_hierarchical()
    {
        var reader = await _factory.ClientForAsync(Roles.Reader);
        var moderator = await _factory.ClientForAsync(Roles.Moderator);
        var root = await _factory.ClientForAsync(Roles.Root);

        (await reader.GetAsync("/products")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await reader.PostAsJsonAsync("/products", NewProduct())).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var (id, _, _) = await CreateAsync(moderator);
        (await moderator.DeleteAsync($"/products/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await moderator.PostAsync($"/products/{id}/restore", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await moderator.PostAsync($"/products/{id}/purge", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await moderator.GetAsync($"/products/{id}/history")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // root inherits moderator and reader rights
        (await root.GetAsync("/products")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await root.PostAsync($"/products/{id}/restore", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await root.GetAsync($"/products/{id}/history")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await root.PostAsync($"/products/{id}/purge", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await root.GetAsync($"/products/{id}/history")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Tokens_signed_with_another_key_are_rejected()
    {
        var handler = new Microsoft.IdentityModel.JsonWebTokens.JsonWebTokenHandler();
        string forged = handler.CreateToken(new Microsoft.IdentityModel.Tokens.SecurityTokenDescriptor
        {
            Issuer = "crudapp", Audience = "crudapp", Expires = DateTime.UtcNow.AddHours(1),
            Claims = new Dictionary<string, object> { ["sub"] = "x", ["role"] = Roles.Root },
            SigningCredentials = new(new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
                Encoding.UTF8.GetBytes("6ceccd7405ef4b00b2630009be568cfa-the-old-committed-key")), "HS256")
        });
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", forged);

        (await client.GetAsync("/products")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ------------------------------------------------------------ HTTP semantics

    [Fact]
    public async Task Create_returns_201_with_location_and_etag_and_ignores_system_fields()
    {
        var moderator = await _factory.ClientForAsync(Roles.Moderator);

        var response = await moderator.PostAsJsonAsync("/products", new
        {
            name = "x", price = 1, id = "chosen", rowVersion = 99, createdBy = "ceo", isDeleted = true, deletedBy = "ghost"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await BodyAsync(response);
        string id = body["id"]!.GetValue<string>();
        id.Should().NotBe("chosen");
        response.Headers.Location!.ToString().Should().Be($"/products/{id}");
        response.Headers.ETag!.Tag.Should().Be("\"1\"");
        body["rowVersion"]!.GetValue<int>().Should().Be(1);
        body["createdBy"]!.GetValue<string>().Should().NotBe("ceo");
        body["isDeleted"]!.GetValue<bool>().Should().BeFalse();
        body["deletedBy"].Should().BeNull();
    }

    [Fact]
    public async Task Get_returns_etag_matching_row_version()
    {
        var client = await _factory.ClientForAsync(Roles.Moderator);
        var (id, etag, _) = await CreateAsync(client);

        var response = await client.GetAsync($"/products/{id}");

        response.Headers.ETag!.Tag.Should().Be(etag).And.Be("\"1\"");
        (await client.GetAsync("/products/nope")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Replace_requires_a_precondition_and_reports_conflicts_correctly()
    {
        var client = await _factory.ClientForAsync(Roles.Moderator);
        var (id, etag, _) = await CreateAsync(client);

        (await client.PutAsJsonAsync($"/products/{id}", NewProduct("no version"))).StatusCode
            .Should().Be((HttpStatusCode)428);

        var stale = new HttpRequestMessage(HttpMethod.Put, $"/products/{id}") { Content = JsonContent.Create(NewProduct("stale")) };
        stale.Headers.IfMatch.Add(new EntityTagHeaderValue("\"9\""));
        (await client.SendAsync(stale)).StatusCode.Should().Be(HttpStatusCode.PreconditionFailed);

        var ok = new HttpRequestMessage(HttpMethod.Put, $"/products/{id}") { Content = JsonContent.Create(NewProduct("fresh")) };
        ok.Headers.IfMatch.Add(new EntityTagHeaderValue(etag));
        var updated = await client.SendAsync(ok);
        updated.StatusCode.Should().Be(HttpStatusCode.OK);
        updated.Headers.ETag!.Tag.Should().Be("\"2\"");

        var bodyVersion = await client.PutAsJsonAsync($"/products/{id}", new { name = "body", price = 1, rowVersion = 1 });
        bodyVersion.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await BodyAsync(bodyVersion))["currentVersion"]!.GetValue<int>().Should().Be(2);
    }

    [Fact]
    public async Task Replace_cannot_rewrite_audit_or_lifecycle_fields()
    {
        var client = await _factory.ClientForAsync(Roles.Moderator);
        var (id, _, created) = await CreateAsync(client);

        var response = await client.PutAsJsonAsync($"/products/{id}", new
        {
            name = "renamed", price = 2, rowVersion = 1,
            createdBy = "ceo", createdAt = "1999-01-01T00:00:00Z", isDeleted = true, deletedBy = "ghost", id = "other"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await BodyAsync(response);
        body["id"]!.GetValue<string>().Should().Be(id);
        body["createdBy"]!.GetValue<string>().Should().Be(created["createdBy"]!.GetValue<string>());
        body["createdAt"]!.GetValue<DateTime>().Should().Be(created["createdAt"]!.GetValue<DateTime>());
        body["isDeleted"]!.GetValue<bool>().Should().BeFalse();
        (await client.GetAsync($"/products/{id}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Deleted_entities_disappear_and_cannot_be_updated()
    {
        var client = await _factory.ClientForAsync(Roles.Moderator);
        var (id, _, _) = await CreateAsync(client);

        (await client.DeleteAsync($"/products/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetAsync($"/products/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.PutAsJsonAsync($"/products/{id}", new { name = "x", rowVersion = 2 })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await client.DeleteAsync($"/products/{id}")).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Concurrent_http_writers_cannot_lose_updates()
    {
        var client = await _factory.ClientForAsync(Roles.Moderator);
        var (id, etag, _) = await CreateAsync(client);

        var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(i =>
        {
            var request = new HttpRequestMessage(HttpMethod.Put, $"/products/{id}") { Content = JsonContent.Create(NewProduct($"w{i}")) };
            request.Headers.IfMatch.Add(new EntityTagHeaderValue(etag));
            return client.SendAsync(request);
        }));

        responses.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.PreconditionFailed).Should().Be(19);
    }

    // ------------------------------------------------------------ validation & errors

    [Fact]
    public async Task Invalid_bodies_return_validation_problems()
    {
        var client = await _factory.ClientForAsync(Roles.Moderator);

        var response = await client.PostAsJsonAsync("/products", new { price = -1 });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var errors = (await BodyAsync(response))["errors"]!.AsObject();
        errors.Select(e => e.Key).Should().Contain(["Name", "Price"]);
    }

    [Fact]
    public async Task Malformed_json_is_a_client_error()
    {
        var client = await _factory.ClientForAsync(Roles.Moderator);
        var response = await client.PostAsync("/products", new StringContent("{bad", Encoding.UTF8, "application/json"));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("limit=0")]
    [InlineData("limit=101")]
    [InlineData("offset=-1")]
    [InlineData("offset=9990&limit=20")]
    public async Task Paging_parameters_are_validated(string query)
    {
        var client = await _factory.ClientForAsync(Roles.Reader);
        (await client.GetAsync($"/products?{query}")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task List_and_search_return_pages()
    {
        var client = await _factory.ClientForAsync(Roles.Moderator);
        for (int i = 0; i < 3; i++)
            await CreateAsync(client, NewProduct($"p{i}"));

        var list = await BodyAsync(await client.GetAsync("/products?offset=1&limit=2"));
        list["total"]!.GetValue<long>().Should().Be(3);
        list["items"]!.AsArray().Should().HaveCount(2);

        var search = await client.GetAsync("/products/search?q=" + Uri.EscapeDataString("kalem x)|(@IsDeleted:{true}"));
        search.StatusCode.Should().Be(HttpStatusCode.OK);
        var hits = await BodyAsync(await client.GetAsync("/products/search?q=kalem"));
        hits["total"]!.GetValue<long>().Should().Be(3);
    }

    [Fact]
    public async Task History_endpoint_exposes_operations_with_old_and_new_values()
    {
        var moderator = await _factory.ClientForAsync(Roles.Moderator);
        var root = await _factory.ClientForAsync(Roles.Root);
        var (id, _, _) = await CreateAsync(moderator);
        await moderator.PutAsJsonAsync($"/products/{id}", new { name = "Silgi", price = 10.5, rowVersion = 1, description = "Kırmızı tükenmez kalemler", category = "ofis", stock = 3, tags = new[] { "a" } });

        var history = (await root.GetFromJsonAsync<JsonArray>($"/products/{id}/history"))!;

        history.Select(e => e!["operation"]!.GetValue<string>()).Should().Equal("Update", "Create");
        var change = history[0]!["changes"]!.AsArray().Single()!;
        change["path"]!.GetValue<string>().Should().Be("Name");
        change["old"]!.GetValue<string>().Should().Be("Kalem");
        change["new"]!.GetValue<string>().Should().Be("Silgi");
    }

    // ------------------------------------------------------------ exposure

    [Fact]
    public async Task OpenApi_and_swagger_are_not_exposed_outside_development()
    {
        var client = _factory.CreateClient();
        (await client.GetAsync("/openapi/v1.json")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync("/swagger/index.html")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Errors_do_not_leak_exception_details()
    {
        var client = await _factory.ClientForAsync(Roles.Moderator);
        var response = await client.PostAsync("/products", new StringContent("{bad", Encoding.UTF8, "application/json"));
        (await response.Content.ReadAsStringAsync()).Should().NotContain("JsonException").And.NotContain(" at ");
    }
}
