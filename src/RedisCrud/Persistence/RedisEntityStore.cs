using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Redis.OM;
using Redis.OM.Searching;
using RedisCrud.History;
using RedisCrud.Hosting;
using StackExchange.Redis;

namespace RedisCrud.Persistence;

/// <summary>
/// Persistence for one entity type on Redis 8 (JSON + Query Engine + Streams).
/// <list type="bullet">
/// <item>Every write is a single Lua script: version compare-and-set, <c>JSON.SET</c> and the history <c>XADD</c> commit together or not at all.</item>
/// <item>Reads by id are one <c>JSON.GET</c>; lists and searches are one <c>FT.SEARCH</c> that returns items and total together.</item>
/// <item>System fields (<see cref="Entity"/>) are owned here; client-supplied values are discarded.</item>
/// </list>
/// All writes to these keys must go through this type: a write that does not bump <c>RowVersion</c>
/// would make the compare-and-set blind to it.
/// </summary>
public sealed class RedisEntityStore<T> where T : Entity, new()
{
    // KEYS[1] document, KEYS[2] history stream
    // ARGV[1] mode (create|write), ARGV[2] expected version, ARGV[3] document JSON,
    // ARGV[4] history max length (0 = no history), ARGV[5..] history entry field/value pairs
    private const string WriteScript = """
        local current = redis.call('JSON.GET', KEYS[1], '$.RowVersion')
        if ARGV[1] == 'create' then
          if current then return {'exists', ''} end
        else
          if not current then return {'notfound', ''} end
          local version = tostring(cjson.decode(current)[1])
          if version ~= ARGV[2] then return {'conflict', version} end
        end
        redis.call('JSON.SET', KEYS[1], '$', ARGV[3])
        if tonumber(ARGV[4]) > 0 then
          redis.call('XADD', KEYS[2], 'MAXLEN', ARGV[4], '*', unpack(ARGV, 5))
        end
        return {'ok', ''}
        """;

    // KEYS[1] document, KEYS[2] history stream; ARGV[1] expected version or '' for unconditional
    private const string PurgeScript = """
        local current = redis.call('JSON.GET', KEYS[1], '$.RowVersion')
        if not current then return {'notfound', ''} end
        local version = tostring(cjson.decode(current)[1])
        if ARGV[1] ~= '' and version ~= ARGV[1] then return {'conflict', version} end
        redis.call('DEL', KEYS[1], KEYS[2])
        return {'ok', ''}
        """;

    private static readonly EntityMetadata<T> Meta = EntityMetadata<T>.Instance;

    private readonly IConnectionMultiplexer _redis;
    private readonly RedisConnectionProvider _provider;
    private readonly ICrudActor _actor;
    private readonly CrudOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<RedisEntityStore<T>> _logger;

    public RedisEntityStore(
        IConnectionMultiplexer redis,
        RedisConnectionProvider provider,
        ICrudActor actor,
        IOptions<CrudOptions> options,
        TimeProvider time,
        ILogger<RedisEntityStore<T>> logger)
    {
        _redis = redis;
        _provider = provider;
        _actor = actor;
        _options = options.Value;
        _time = time;
        _logger = logger;
    }

    private IDatabase Db => _redis.GetDatabase();

    private static JsonSerializerOptions Json => StorageJson.Options;

    /// <summary>Ids are server-generated (UUIDv7, 32 hex chars); anything else cannot exist and is rejected without a round trip.</summary>
    public static bool IsValidId(string? id) =>
        !string.IsNullOrEmpty(id) && id.Length <= 64 && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    // ---------------------------------------------------------------- reads

    public async Task<T?> GetAsync(string id, bool includeDeleted = false)
    {
        var stored = await LoadAsync(id);
        if (stored is null || (stored.Value.Entity.IsDeleted && !includeDeleted))
            return null;
        return stored.Value.Entity;
    }

    /// <summary>Active entities ordered by creation time.</summary>
    public Task<Page<T>> ListAsync(int offset, int limit) =>
        SearchRawAsync(SearchQuery.ActiveFilter, offset, limit, sortByCreatedAt: true);

    /// <summary>Full-text search over <c>[Searchable]</c> properties, ordered by relevance.</summary>
    public Task<Page<T>> SearchAsync(string text, int offset, int limit)
    {
        string? query = SearchQuery.BuildFullText(Meta.SearchableFields, text);
        return query is null
            ? Task.FromResult(new Page<T>([], offset, limit, 0))
            : SearchRawAsync(query, offset, limit, sortByCreatedAt: false);
    }

    /// <summary>
    /// Redis OM LINQ over active entities, for application-specific read queries.
    /// Use it for reads only; writes must go through this store.
    /// </summary>
    public IRedisCollection<T> Query() => _provider.RedisCollection<T>(saveState: false).Where(e => !e.IsDeleted);

    public async Task<IReadOnlyList<HistoryEvent>> GetHistoryAsync(string id, int count)
    {
        if (!IsValidId(id))
            return [];

        StreamEntry[] entries = await Db.StreamRangeAsync(Meta.HistoryKey(id), "-", "+", count, Order.Descending);
        return entries.Select(ToHistoryEvent).ToList();
    }

    // ---------------------------------------------------------------- writes

    public async Task<WriteResult<T>> CreateAsync(T input)
    {
        var now = Now();
        input.Id = Guid.CreateVersion7().ToString("N");
        input.RowVersion = 1;
        input.CreatedAt = input.UpdatedAt = now;
        input.CreatedBy = input.UpdatedBy = _actor.Id;
        input.IsDeleted = false;
        input.DeletedAt = null;
        input.DeletedBy = null;

        var after = ToNode(input);
        var outcome = await WriteAsync(input.Id, "create", 0, after, HistoryOperation.Create, before: null);
        return outcome.Status == WriteStatus.Ok ? WriteResult<T>.Success(input) : outcome;
    }

    /// <summary>Full replacement of client-owned fields, conditional on <paramref name="expectedVersion"/>.</summary>
    public async Task<WriteResult<T>> ReplaceAsync(string id, T input, int expectedVersion)
    {
        var stored = await LoadAsync(id);
        if (stored is null)
            return WriteResult<T>.Missing();

        var (current, before) = stored.Value;
        if (current.IsDeleted)
            return WriteResult<T>.Invalid("The entity is deleted. Restore it before updating.");
        if (current.RowVersion != expectedVersion)
            return WriteResult<T>.Conflict(current.RowVersion);

        foreach (var property in Meta.SensitiveProperties)
        {
            if (property.GetValue(input) is null)
                property.SetValue(input, property.GetValue(current));
        }

        input.CopySystemFieldsFrom(current);
        var after = ToNode(input);
        if (JsonDiff.Diff(before, after, Entity.SystemProperties, Meta.SensitiveNames).Count == 0)
            return WriteResult<T>.Success(current);

        input.RowVersion = current.RowVersion + 1;
        input.UpdatedAt = Now();
        input.UpdatedBy = _actor.Id;
        after = ToNode(input);

        var outcome = await WriteAsync(id, "write", current.RowVersion, after, HistoryOperation.Update, before);
        return outcome.Status == WriteStatus.Ok ? WriteResult<T>.Success(input) : outcome;
    }

    /// <summary>
    /// Soft delete (default) or permanent delete when <see cref="CrudOptions.SoftDelete"/> is off.
    /// <paramref name="expectedVersion"/> is optional; the write is compare-and-set against the version read either way.
    /// </summary>
    public async Task<WriteResult<T>> DeleteAsync(string id, int? expectedVersion = null)
    {
        if (!_options.SoftDelete)
            return await PurgeAsync(id, expectedVersion);

        return await TransitionAsync(id, expectedVersion, HistoryOperation.Delete, entity =>
        {
            if (entity.IsDeleted)
                return "The entity is already deleted.";
            entity.IsDeleted = true;
            entity.DeletedAt = Now();
            entity.DeletedBy = _actor.Id;
            return null;
        });
    }

    public Task<WriteResult<T>> RestoreAsync(string id, int? expectedVersion = null) =>
        TransitionAsync(id, expectedVersion, HistoryOperation.Restore, entity =>
        {
            if (!entity.IsDeleted)
                return "The entity is not deleted.";
            entity.IsDeleted = false;
            entity.DeletedAt = null;
            entity.DeletedBy = null;
            return null;
        });

    /// <summary>Removes the document and its history. Irreversible; the purge itself is recorded only in the application log.</summary>
    public async Task<WriteResult<T>> PurgeAsync(string id, int? expectedVersion = null)
    {
        if (!IsValidId(id))
            return WriteResult<T>.Missing();

        var result = (RedisResult[])(await Db.ScriptEvaluateAsync(
            PurgeScript,
            [Meta.DocumentKey(id), Meta.HistoryKey(id)],
            [expectedVersion?.ToString() ?? string.Empty]))!;

        var outcome = ToWriteResult((string)result[0]!, (string)result[1]!);
        if (outcome.Status == WriteStatus.Ok)
            _logger.LogWarning("Purged {Entity} {Id} (document and history) by {Actor}", typeof(T).Name, id, _actor.Id);
        return outcome;
    }

    // ---------------------------------------------------------------- internals

    private async Task<WriteResult<T>> TransitionAsync(string id, int? expectedVersion, HistoryOperation operation, Func<T, string?> apply)
    {
        var stored = await LoadAsync(id);
        if (stored is null)
            return WriteResult<T>.Missing();

        var (entity, before) = stored.Value;
        if (expectedVersion is { } expected && expected != entity.RowVersion)
            return WriteResult<T>.Conflict(entity.RowVersion);

        if (apply(entity) is { } error)
            return WriteResult<T>.Invalid(error);

        int readVersion = entity.RowVersion;
        entity.RowVersion = readVersion + 1;
        entity.UpdatedAt = Now();
        entity.UpdatedBy = _actor.Id;

        var outcome = await WriteAsync(id, "write", readVersion, ToNode(entity), operation, before);
        return outcome.Status == WriteStatus.Ok ? WriteResult<T>.Success(entity) : outcome;
    }

    private async Task<WriteResult<T>> WriteAsync(string id, string mode, int expectedVersion, JsonObject after, HistoryOperation operation, JsonObject? before)
    {
        var args = new List<RedisValue>
        {
            mode,
            expectedVersion.ToString(),
            after.ToJsonString(Json),
            _options.HistoryMaxLength
        };

        if (_options.HistoryMaxLength > 0)
        {
            var changes = JsonDiff.Diff(before, after, Entity.SystemProperties, Meta.SensitiveNames);
            args.Add("op"); args.Add(operation.ToString());
            args.Add("v"); args.Add(after[nameof(Entity.RowVersion)]!.ToJsonString());
            args.Add("by"); args.Add(_actor.Id);
            args.Add("changes"); args.Add(JsonSerializer.Serialize(changes, HistoryJson));
        }

        var result = (RedisResult[])(await Db.ScriptEvaluateAsync(
            WriteScript,
            [Meta.DocumentKey(id), Meta.HistoryKey(id)],
            args.ToArray()))!;

        return ToWriteResult((string)result[0]!, (string)result[1]!);
    }

    private static WriteResult<T> ToWriteResult(string status, string detail) => status switch
    {
        "ok" => new WriteResult<T>(WriteStatus.Ok),
        "notfound" => WriteResult<T>.Missing(),
        "conflict" => WriteResult<T>.Conflict(int.Parse(detail)),
        "exists" => WriteResult<T>.Invalid("An entity with this id already exists."),
        _ => throw new InvalidOperationException($"Unexpected script result '{status}'.")
    };

    private async Task<(T Entity, JsonObject Node)?> LoadAsync(string id)
    {
        if (!IsValidId(id))
            return null;

        var raw = await Db.ExecuteAsync("JSON.GET", Meta.DocumentKey(id));
        if (raw.IsNull)
            return null;

        string json = raw.ToString();
        var node = JsonNode.Parse(json)!.AsObject();
        var entity = JsonSerializer.Deserialize<T>(json, Json)!;
        return (entity, node);
    }

    private async Task<Page<T>> SearchRawAsync(string query, int offset, int limit, bool sortByCreatedAt)
    {
        var args = new List<object> { Meta.IndexName, query };
        if (sortByCreatedAt)
            args.AddRange(["SORTBY", nameof(Entity.CreatedAt), "ASC"]);
        args.AddRange(["LIMIT", offset, limit, "DIALECT", 2]);

        var reply = (RedisResult[])(await Db.ExecuteAsync("FT.SEARCH", args))!;
        long total = (long)reply[0];
        var items = new List<T>(Math.Max(0, (reply.Length - 1) / 2));

        for (int i = 1; i + 1 < reply.Length; i += 2)
        {
            var fields = (RedisResult[])reply[i + 1]!;
            for (int f = 0; f + 1 < fields.Length; f += 2)
            {
                if ((string)fields[f]! == "$")
                    items.Add(JsonSerializer.Deserialize<T>((string)fields[f + 1]!, Json)!);
            }
        }

        return new Page<T>(items, offset, limit, total);
    }

    private static JsonObject ToNode(T entity) => JsonSerializer.SerializeToNode(entity, Json)!.AsObject();

    /// <summary>Millisecond precision: that is what the Redis OM DateTime format stores, so responses match later reads.</summary>
    private DateTime Now()
    {
        var now = _time.GetUtcNow().UtcDateTime;
        return now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMillisecond));
    }

    private static readonly JsonSerializerOptions HistoryJson = new(JsonSerializerDefaults.Web);

    private static HistoryEvent ToHistoryEvent(StreamEntry entry)
    {
        string eventId = entry.Id!;
        long ms = long.Parse(eventId.AsSpan(0, eventId.IndexOf('-')));
        string changes = entry["changes"].IsNull ? "[]" : (string)entry["changes"]!;

        return new HistoryEvent(
            eventId,
            DateTimeOffset.FromUnixTimeMilliseconds(ms),
            Enum.Parse<HistoryOperation>((string)entry["op"]!),
            (int)entry["v"],
            (string)entry["by"]!,
            JsonSerializer.Deserialize<List<FieldChange>>(changes, HistoryJson) ?? []);
    }
}
