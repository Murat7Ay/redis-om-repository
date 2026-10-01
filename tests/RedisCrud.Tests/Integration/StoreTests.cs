using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RedisCrud.History;
using RedisCrud.Hosting;
using RedisCrud.Persistence;
using Redis.OM;

namespace RedisCrud.Tests.Integration;

[Collection(RedisCollection.Name)]
public class StoreTests(RedisFixture redis) : IAsyncLifetime
{
    public Task InitializeAsync() => redis.ResetAsync<Widget>();
    public Task DisposeAsync() => Task.CompletedTask;

    private RedisEntityStore<Widget> Store(string actor = "alice", CrudOptions? options = null) => redis.Store<Widget>(actor, options);

    private async Task<Widget> CreateAsync(string name = "w", string description = "", string actor = "alice")
    {
        var result = await Store(actor).CreateAsync(new Widget { Name = name, Description = description, Price = 1m });
        result.Status.Should().Be(WriteStatus.Ok);
        return result.Entity!;
    }

    // ------------------------------------------------------------ create

    [Fact]
    public async Task Create_owns_system_fields_and_ignores_client_values()
    {
        var input = new Widget
        {
            Name = "w", Id = "client-id", RowVersion = 42, CreatedBy = "ceo", CreatedAt = new DateTime(1999, 1, 1),
            IsDeleted = true, DeletedBy = "ghost"
        };

        var created = (await Store("alice").CreateAsync(input)).Entity!;

        created.Id.Should().MatchRegex("^[0-9a-f]{32}$").And.NotBe("client-id");
        created.RowVersion.Should().Be(1);
        created.CreatedBy.Should().Be("alice");
        created.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(10));
        created.IsDeleted.Should().BeFalse();
        created.DeletedBy.Should().BeNull();

        var stored = await Store().GetAsync(created.Id);
        stored.Should().BeEquivalentTo(created);
    }

    [Fact]
    public async Task Create_handles_default_DateTime_on_any_host_time_zone()
    {
        var created = await Store().CreateAsync(new Widget { Name = "w", Released = default });
        created.Status.Should().Be(WriteStatus.Ok);
        (await Store().GetAsync(created.Entity!.Id))!.Released.Should().Be(DateTime.MinValue);
    }

    [Fact]
    public async Task Nested_objects_collections_and_dictionaries_round_trip()
    {
        var created = (await Store().CreateAsync(new Widget
        {
            Name = "w", Tags = ["a", "b"], Counters = new() { ["x"] = 1 }, Address = new Address { City = "İzmir" }, Kind = WidgetKind.Large
        })).Entity!;

        var stored = (await Store().GetAsync(created.Id))!;
        stored.Tags.Should().Equal("a", "b");
        stored.Counters.Should().ContainKey("x").WhoseValue.Should().Be(1);
        stored.Address!.City.Should().Be("İzmir");
        stored.Kind.Should().Be(WidgetKind.Large);
    }

    // ------------------------------------------------------------ replace & concurrency

    [Fact]
    public async Task Replace_bumps_version_and_preserves_creation_audit()
    {
        var created = await CreateAsync(actor: "alice");

        var result = await Store("bob").ReplaceAsync(created.Id,
            new Widget { Name = "renamed", CreatedBy = "forged", CreatedAt = new DateTime(2000, 1, 1), IsDeleted = true }, expectedVersion: 1);

        result.Status.Should().Be(WriteStatus.Ok);
        var stored = (await Store().GetAsync(created.Id))!;
        stored.Name.Should().Be("renamed");
        stored.RowVersion.Should().Be(2);
        stored.CreatedBy.Should().Be("alice");
        stored.CreatedAt.Should().Be(created.CreatedAt);
        stored.UpdatedBy.Should().Be("bob");
        stored.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task Replace_with_stale_version_conflicts_and_reports_current_version()
    {
        var created = await CreateAsync();
        await Store().ReplaceAsync(created.Id, new Widget { Name = "v2" }, 1);

        var result = await Store().ReplaceAsync(created.Id, new Widget { Name = "lost update" }, 1);

        result.Status.Should().Be(WriteStatus.VersionConflict);
        result.CurrentVersion.Should().Be(2);
        (await Store().GetAsync(created.Id))!.Name.Should().Be("v2");
    }

    [Fact]
    public async Task Concurrent_writers_with_the_same_version_produce_exactly_one_winner()
    {
        var created = await CreateAsync();

        var results = await Task.WhenAll(Enumerable.Range(0, 25).Select(i =>
            Task.Run(() => Store($"writer{i}").ReplaceAsync(created.Id, new Widget { Name = $"w{i}" }, 1))));

        results.Count(r => r.Status == WriteStatus.Ok).Should().Be(1);
        results.Count(r => r.Status == WriteStatus.VersionConflict).Should().Be(24);

        var stored = (await Store().GetAsync(created.Id))!;
        stored.RowVersion.Should().Be(2);
        var winner = results.Single(r => r.Status == WriteStatus.Ok).Entity!;
        stored.Name.Should().Be(winner.Name);

        var history = await Store().GetHistoryAsync(created.Id, 100);
        history.Should().HaveCount(2, "losers must not leave history entries behind");
        history[0].Changes.Single(c => c.Path == "Name").New!.GetValue<string>().Should().Be(winner.Name);
    }

    [Fact]
    public async Task Replace_that_changes_nothing_does_not_bump_version_or_write_history()
    {
        var created = await CreateAsync(name: "same");

        var result = await Store().ReplaceAsync(created.Id, new Widget { Name = "same", Price = 1m }, 1);

        result.Status.Should().Be(WriteStatus.Ok);
        result.Entity!.RowVersion.Should().Be(1);
        (await Store().GetHistoryAsync(created.Id, 10)).Should().HaveCount(1);
    }

    [Fact]
    public async Task Sensitive_property_is_kept_when_omitted_and_masked_in_history()
    {
        var created = (await Store().CreateAsync(new Widget { Name = "w", Secret = "s1" })).Entity!;

        await Store().ReplaceAsync(created.Id, new Widget { Name = "w2", Secret = null }, 1);
        (await Store().GetAsync(created.Id))!.Secret.Should().Be("s1");

        await Store().ReplaceAsync(created.Id, new Widget { Name = "w2", Secret = "s2" }, 2);
        (await Store().GetAsync(created.Id))!.Secret.Should().Be("s2");

        var history = await Store().GetHistoryAsync(created.Id, 10);
        var secretChange = history[0].Changes.Should().ContainSingle(c => c.Path == "Secret").Subject;
        secretChange.New!.GetValue<string>().Should().Be(JsonDiff.Mask);
        history.SelectMany(h => h.Changes).Select(c => c.New?.ToJsonString() + c.Old?.ToJsonString())
            .Should().NotContain(s => s.Contains("s1") || s.Contains("s2"));
    }

    // ------------------------------------------------------------ lifecycle

    [Fact]
    public async Task Soft_delete_hides_entity_blocks_updates_and_can_be_restored()
    {
        var created = await CreateAsync(description: "lifecycle entity");

        (await Store("mod").DeleteAsync(created.Id)).Status.Should().Be(WriteStatus.Ok);

        (await Store().GetAsync(created.Id)).Should().BeNull();
        var deleted = (await Store().GetAsync(created.Id, includeDeleted: true))!;
        deleted.IsDeleted.Should().BeTrue();
        deleted.DeletedBy.Should().Be("mod");
        deleted.RowVersion.Should().Be(2);
        (await Store().ListAsync(0, 100)).Items.Should().NotContain(w => w.Id == created.Id);
        (await Store().SearchAsync("lifecycle", 0, 100)).Items.Should().BeEmpty();
        (await Store().ReplaceAsync(created.Id, new Widget { Name = "x" }, 2)).Status.Should().Be(WriteStatus.InvalidState);
        (await Store().DeleteAsync(created.Id)).Status.Should().Be(WriteStatus.InvalidState);

        (await Store().RestoreAsync(created.Id)).Status.Should().Be(WriteStatus.Ok);
        var restored = (await Store().GetAsync(created.Id))!;
        restored.IsDeleted.Should().BeFalse();
        restored.DeletedAt.Should().BeNull();
        restored.RowVersion.Should().Be(3);
        (await Store().RestoreAsync(created.Id)).Status.Should().Be(WriteStatus.InvalidState);
    }

    [Fact]
    public async Task Delete_honours_expected_version()
    {
        var created = await CreateAsync();
        var result = await Store().DeleteAsync(created.Id, expectedVersion: 7);
        result.Status.Should().Be(WriteStatus.VersionConflict);
        (await Store().GetAsync(created.Id)).Should().NotBeNull();
    }

    [Fact]
    public async Task Purge_removes_document_and_history()
    {
        var created = await CreateAsync();
        await Store().DeleteAsync(created.Id);

        (await Store().PurgeAsync(created.Id)).Status.Should().Be(WriteStatus.Ok);

        (await Store().GetAsync(created.Id, includeDeleted: true)).Should().BeNull();
        (await redis.Redis.GetDatabase().KeyExistsAsync(EntityMetadata<Widget>.Instance.HistoryKey(created.Id))).Should().BeFalse();
        (await Store().PurgeAsync(created.Id)).Status.Should().Be(WriteStatus.NotFound);
    }

    [Fact]
    public async Task Hard_delete_mode_purges()
    {
        var created = await CreateAsync();
        var store = Store(options: new CrudOptions { SoftDelete = false });

        (await store.DeleteAsync(created.Id)).Status.Should().Be(WriteStatus.Ok);
        (await store.GetAsync(created.Id, includeDeleted: true)).Should().BeNull();
    }

    [Theory]
    [InlineData("does-not-exist")]
    [InlineData("../x")]
    [InlineData("a b")]
    [InlineData("TestWidget:1")]
    public async Task Unknown_or_malformed_ids_are_not_found(string id)
    {
        (await Store().GetAsync(id)).Should().BeNull();
        (await Store().ReplaceAsync(id, new Widget(), 1)).Status.Should().Be(WriteStatus.NotFound);
        (await Store().DeleteAsync(id)).Status.Should().Be(WriteStatus.NotFound);
        (await Store().PurgeAsync(id)).Status.Should().Be(WriteStatus.NotFound);
        (await Store().GetHistoryAsync(id, 10)).Should().BeEmpty();
    }

    // ------------------------------------------------------------ history

    [Fact]
    public async Task History_records_every_write_with_actor_version_and_values()
    {
        var created = await CreateAsync(name: "v1", actor: "alice");
        await Store("bob").ReplaceAsync(created.Id, new Widget { Name = "v2", Price = 1m, Tags = ["t"] }, 1);
        await Store("carol").DeleteAsync(created.Id);
        await Store("dave").RestoreAsync(created.Id);

        var history = await Store().GetHistoryAsync(created.Id, 10);

        history.Select(h => (h.Operation, h.Version, h.Actor)).Should().Equal(
            (HistoryOperation.Restore, 4, "dave"),
            (HistoryOperation.Delete, 3, "carol"),
            (HistoryOperation.Update, 2, "bob"),
            (HistoryOperation.Create, 1, "alice"));
        history[2].Changes.Select(c => c.Path).Should().BeEquivalentTo("Name", "Tags");
        history.Should().BeInDescendingOrder(h => h.EventId, StringComparer.Ordinal);
        history[0].At.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task History_is_trimmed_exactly_to_the_configured_length()
    {
        var store = Store(options: new CrudOptions { HistoryMaxLength = 3 });
        var created = (await store.CreateAsync(new Widget { Name = "0" })).Entity!;
        for (int v = 1; v <= 5; v++)
            await store.ReplaceAsync(created.Id, new Widget { Name = v.ToString() }, v);

        var history = await store.GetHistoryAsync(created.Id, 100);
        history.Select(h => h.Version).Should().Equal(6, 5, 4);
    }

    [Fact]
    public async Task History_can_be_disabled()
    {
        var store = Store(options: new CrudOptions { HistoryMaxLength = 0 });
        var created = (await store.CreateAsync(new Widget { Name = "0" })).Entity!;
        await store.ReplaceAsync(created.Id, new Widget { Name = "1" }, 1);
        (await store.GetHistoryAsync(created.Id, 10)).Should().BeEmpty();
    }

    // ------------------------------------------------------------ list, search, query

    [Fact]
    public async Task List_pages_are_stable_ordered_by_creation_and_report_total()
    {
        var ids = new List<string>();
        for (int i = 0; i < 7; i++)
            ids.Add((await CreateAsync(name: $"n{i}")).Id);
        await Store().DeleteAsync(ids[3]);
        await Store().ReplaceAsync(ids[0], new Widget { Name = "touched" }, 1); // updates must not reorder pages

        var pages = new List<Page<Widget>>();
        for (int offset = 0; offset < 9; offset += 3)
            pages.Add(await Store().ListAsync(offset, 3));

        pages.Should().AllSatisfy(p => p.Total.Should().Be(6));
        pages.SelectMany(p => p.Items).Select(w => w.Id).Should().Equal(ids.Where((_, i) => i != 3));
    }

    [Fact]
    public async Task Search_matches_word_prefixes_across_searchable_fields_with_and_semantics()
    {
        var pen = (await Store().CreateAsync(new Widget { Name = "pen", Description = "Kırmızı tükenmez kalemler", Notes = "ofis" })).Entity!;
        var book = (await Store().CreateAsync(new Widget { Name = "book", Description = "Defter", Notes = "kalem kutusu dahil" })).Entity!;
        await CreateAsync(name: "other", description: "unrelated");

        (await Store().SearchAsync("kalem", 0, 10)).Items.Select(w => w.Id).Should().BeEquivalentTo([pen.Id, book.Id]);
        (await Store().SearchAsync("kalem ofis", 0, 10)).Items.Should().ContainSingle().Which.Id.Should().Be(pen.Id);
        (await Store().SearchAsync("KIRMIZI", 0, 10)).Items.Should().ContainSingle().Which.Id.Should().Be(pen.Id);
    }

    [Fact]
    public async Task Search_handles_turkish_dotted_and_dotless_i_in_both_directions()
    {
        var city = (await Store().CreateAsync(new Widget { Name = "c", Description = "İstanbul ılık" })).Entity!;

        (await Store().SearchAsync("istanbul", 0, 10)).Items.Should().ContainSingle().Which.Id.Should().Be(city.Id);
        (await Store().SearchAsync("ISTANBUL", 0, 10)).Items.Should().ContainSingle().Which.Id.Should().Be(city.Id);
        (await Store().SearchAsync("ILIK", 0, 10)).Items.Should().ContainSingle().Which.Id.Should().Be(city.Id);
        (await Store().SearchAsync("ilik", 0, 10)).Items.Should().BeEmpty("ASCII folding of ı is a documented limitation");
    }

    [Theory]
    [InlineData("x)|(@IsDeleted:{true}")]
    [InlineData("-kalem")]
    [InlineData("*")]
    [InlineData("@Name:{pen}")]
    [InlineData("\"unbalanced")]
    public async Task Search_input_cannot_inject_query_syntax(string payload)
    {
        var hidden = await CreateAsync(name: "hidden", description: "true IsDeleted x");
        await Store().DeleteAsync(hidden.Id);

        var page = await Store().SearchAsync(payload, 0, 10);

        page.Items.Should().NotContain(w => w.Id == hidden.Id);
    }

    [Fact]
    public async Task Search_paginates_with_total()
    {
        for (int i = 0; i < 5; i++)
            await CreateAsync(name: $"p{i}", description: "sayfalama testi");

        var first = await Store().SearchAsync("sayfalama", 0, 2);
        var rest = await Store().SearchAsync("sayfalama", 2, 10);

        first.Total.Should().Be(5);
        first.Items.Should().HaveCount(2);
        rest.Items.Should().HaveCount(3);
        first.Items.Select(w => w.Id).Should().NotIntersectWith(rest.Items.Select(w => w.Id));
    }

    [Fact]
    public async Task Linq_query_excludes_soft_deleted_entities()
    {
        var kept = await CreateAsync(name: "linq-kept");
        var gone = await CreateAsync(name: "linq-gone");
        await Store().DeleteAsync(gone.Id);

        var results = await Store().Query().Where(w => w.Name == "linq-kept" || w.Name == "linq-gone").ToListAsync();

        results.Select(w => w.Id).Should().Equal(kept.Id);
    }

    // ------------------------------------------------------------ index provisioning

    [Fact]
    public async Task Stale_index_is_detected_and_recreated_without_losing_documents()
    {
        await redis.ResetAsync<Gadget>();
        var gadget = (await redis.Store<Gadget>().CreateAsync(new Gadget { Name = "g", Description = "drift probe" })).Entity!;

        // Simulate an older deployment whose index lacked the Description field.
        var db = redis.Redis.GetDatabase();
        await db.ExecuteAsync("FT.DROPINDEX", "gadget-idx");
        await db.ExecuteAsync("FT.CREATE", "gadget-idx", "ON", "JSON", "PREFIX", "1", "TestGadget:", "SCHEMA", "$.Name", "AS", "Name", "TAG");
        (await redis.Provider.Connection.IsIndexCurrentAsync(typeof(Gadget))).Should().BeFalse();

        var registry = new EntityRegistry();
        registry.Add<Gadget>();
        var service = new IndexProvisioningService(redis.Provider, registry, Options.Create(new CrudOptions()), NullLogger<IndexProvisioningService>.Instance);
        await service.StartAsync(CancellationToken.None);

        (await redis.Provider.Connection.IsIndexCurrentAsync(typeof(Gadget))).Should().BeTrue();
        var found = await WaitForAsync(() => redis.Store<Gadget>().SearchAsync("drift", 0, 10), p => p.Total == 1);
        found.Items.Single().Id.Should().Be(gadget.Id);
    }

    [Fact]
    public async Task Stale_index_fails_startup_when_recreation_is_disabled()
    {
        await redis.ResetAsync<Gadget>();
        var db = redis.Redis.GetDatabase();
        await db.ExecuteAsync("FT.DROPINDEX", "gadget-idx");
        await db.ExecuteAsync("FT.CREATE", "gadget-idx", "ON", "JSON", "PREFIX", "1", "TestGadget:", "SCHEMA", "$.Name", "AS", "Name", "TAG");

        var registry = new EntityRegistry();
        registry.Add<Gadget>();
        var service = new IndexProvisioningService(redis.Provider, registry,
            Options.Create(new CrudOptions { RecreateStaleIndexes = false }), NullLogger<IndexProvisioningService>.Instance);

        await FluentActions.Awaiting(() => service.StartAsync(CancellationToken.None)).Should().ThrowAsync<InvalidOperationException>();
        await redis.ResetAsync<Gadget>();
    }

    private static async Task<T> WaitForAsync<T>(Func<Task<T>> probe, Func<T, bool> done)
    {
        for (int attempt = 0; ; attempt++)
        {
            var value = await probe();
            if (done(value) || attempt == 50)
                return value;
            await Task.Delay(100);
        }
    }
}
