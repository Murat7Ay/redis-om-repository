using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using RedisCrud.Endpoints;
using RedisCrud.Persistence;

namespace RedisCrud.Tests.Unit;

public class MetadataAndSerializationTests
{
    [Fact]
    public void Metadata_follows_document_attribute_and_redis_om_conventions()
    {
        var meta = EntityMetadata<Widget>.Instance;

        meta.KeyPrefix.Should().Be("TestWidget");
        meta.IndexName.Should().Be("widget-idx");
        meta.DocumentKey("abc").Should().Be("TestWidget:abc");
        meta.HistoryKey("abc").Should().Be("history:TestWidget:abc").And.NotStartWith(meta.KeyPrefix);
        meta.SearchableFields.Should().BeEquivalentTo("Description", "Notes");
        meta.SensitiveNames.Should().BeEquivalentTo("Secret");
    }

    [Fact]
    public void Entity_without_document_attribute_is_rejected() =>
        FluentActions.Invoking(() => EntityMetadata<NotADocument>.Instance)
            .Should().Throw<TypeInitializationException>()
            .WithInnerException<InvalidOperationException>();

    [Theory]
    [InlineData("01a0f82db65b7fdcb5f51b19831b4ddc", true)]
    [InlineData("abc-DEF_123", true)]
    [InlineData("", false)]
    [InlineData("../etc", false)]
    [InlineData("a b", false)]
    [InlineData("TestWidget:abc", false)]
    [InlineData("{abc}", false)]
    public void Ids_are_restricted_to_a_safe_alphabet(string id, bool valid) =>
        RedisEntityStore<Widget>.IsValidId(id).Should().Be(valid);

    [Fact]
    public void Sensitive_properties_are_write_only_over_http()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver().WithAddedModifier(SensitivePropertyJsonModifier.Apply)
        };

        JsonSerializer.Serialize(new Widget { Name = "w", Secret = "s3cr3t" }, options).Should().NotContain("s3cr3t").And.NotContain("secret");
        JsonSerializer.Deserialize<Widget>("""{"name":"w","secret":"in"}""", options)!.Secret.Should().Be("in");
    }
}
