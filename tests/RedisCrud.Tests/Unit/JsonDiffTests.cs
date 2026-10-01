using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Redis.OM;
using RedisCrud.History;

namespace RedisCrud.Tests.Unit;

public class JsonDiffTests
{
    private static readonly HashSet<string> NoSensitive = [];

    private static JsonObject Node(Widget w)
    {
        RedisSerializationSettings.UseUtcTime();
        return JsonSerializer.SerializeToNode(w, RedisCrud.Persistence.StorageJson.Options)!.AsObject();
    }

    private static List<FieldChange> Diff(Widget before, Widget after, IReadOnlySet<string>? sensitive = null) =>
        JsonDiff.Diff(Node(before), Node(after), Entity.SystemProperties, sensitive ?? NoSensitive);

    [Fact]
    public void Records_old_and_new_values_for_scalars()
    {
        var changes = Diff(new Widget { Name = "Alpha", Price = 10m }, new Widget { Name = "Beta", Price = 20m });

        changes.Should().ContainSingle(c => c.Path == "Name" && c.Old!.GetValue<string>() == "Alpha" && c.New!.GetValue<string>() == "Beta");
        changes.Should().ContainSingle(c => c.Path == "Price" && c.Old!.GetValue<decimal>() == 10m && c.New!.GetValue<decimal>() == 20m);
    }

    [Fact]
    public void Identical_entities_produce_no_changes() =>
        Diff(new Widget { Name = "Same", Tags = ["a"] }, new Widget { Name = "Same", Tags = ["a"] }).Should().BeEmpty();

    [Fact]
    public void Nested_object_changes_use_dotted_paths()
    {
        var changes = Diff(
            new Widget { Address = new Address { City = "Ankara", Street = "A" } },
            new Widget { Address = new Address { City = "İzmir", Street = "A" } });

        changes.Should().ContainSingle().Which.Path.Should().Be("Address.City");
    }

    [Fact]
    public void Null_transitions_are_distinguishable_from_empty_values()
    {
        var changes = Diff(new Widget { Address = null, Secret = null }, new Widget { Address = new Address { City = "Bursa" } });

        var address = changes.Should().ContainSingle(c => c.Path == "Address").Subject;
        address.Old.Should().BeNull();
        address.New!["City"]!.GetValue<string>().Should().Be("Bursa");
    }

    [Fact]
    public void Collections_are_tracked_as_whole_values()
    {
        var changes = Diff(new Widget { Tags = ["a"] }, new Widget { Tags = ["a", "b"] });

        var tags = changes.Should().ContainSingle().Subject;
        tags.Path.Should().Be("Tags");
        tags.Old!.ToJsonString().Should().Be("""["a"]""");
        tags.New!.ToJsonString().Should().Be("""["a","b"]""");
    }

    [Fact]
    public void Dictionary_entries_are_tracked_per_key()
    {
        var changes = Diff(
            new Widget { Counters = new() { ["a"] = 1, ["b"] = 2 } },
            new Widget { Counters = new() { ["a"] = 1, ["b"] = 3, ["c"] = 4 } });

        changes.Select(c => c.Path).Should().BeEquivalentTo("Counters.b", "Counters.c");
        changes.Single(c => c.Path == "Counters.c").Old.Should().BeNull();
    }

    [Fact]
    public void Decimal_scale_differences_are_not_changes() =>
        Diff(new Widget { Price = 10.5m }, new Widget { Price = 10.50m }).Should().BeEmpty();

    [Fact]
    public void Enum_changes_are_recorded() =>
        Diff(new Widget { Kind = WidgetKind.Small }, new Widget { Kind = WidgetKind.Large })
            .Should().ContainSingle(c => c.Path == "Kind");

    [Fact]
    public void Sensitive_values_are_masked_on_both_sides()
    {
        var changes = Diff(new Widget { Secret = "old" }, new Widget { Secret = "new" }, new HashSet<string> { "Secret" });

        var secret = changes.Should().ContainSingle().Subject;
        secret.Old!.GetValue<string>().Should().Be(JsonDiff.Mask);
        secret.New!.GetValue<string>().Should().Be(JsonDiff.Mask);
        JsonSerializer.Serialize(changes).Should().NotContain("old").And.NotContain("new\"");
    }

    [Fact]
    public void System_fields_are_ignored()
    {
        var changes = Diff(
            new Widget { Id = "1", RowVersion = 1, CreatedBy = "a", IsDeleted = false },
            new Widget { Id = "2", RowVersion = 9, CreatedBy = "b", IsDeleted = true, DeletedAt = DateTime.UtcNow });

        changes.Should().BeEmpty();
    }

    [Fact]
    public void Output_is_culture_invariant()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            var change = Diff(new Widget { Price = 10.5m }, new Widget { Price = 11.25m }).Single();
            change.Old!.ToJsonString().Should().Be("10.5");
            change.New!.ToJsonString().Should().Be("11.25");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Creation_diff_lists_every_client_field_with_null_old_values()
    {
        var changes = JsonDiff.Diff(null, Node(new Widget { Name = "n" }), Entity.SystemProperties, NoSensitive);

        changes.Should().Contain(c => c.Path == "Name" && c.Old == null);
        changes.Should().NotContain(c => Entity.SystemProperties.Contains(c.Path));
    }
}
