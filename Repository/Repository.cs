using System.Security.Claims;
using CrudApp.Entity;
using CrudApp.Enums;
using CrudApp.Models;
using Redis.OM;
using Redis.OM.Searching;
using StackExchange.Redis;

namespace CrudApp.Repository;

public class Repository<T> : IRepository<T> where T : class, IEntity<T>, new()
{
    private readonly IRedisCollection<T> _redisCollection;
    private readonly IDatabase _database;
    private readonly Guid _traceId;
    private readonly string _userCode;
    private const int MaxEntityCount = 1000;

    public Repository(RedisConnectionProvider provider, IDatabase database, IHttpContextAccessor httpContextAccessor)
    {
        _database = database;
        _redisCollection = provider.RedisCollection<T>();
        Claim? hashClaim = httpContextAccessor.HttpContext?.User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Hash);
        _traceId = string.IsNullOrEmpty(hashClaim?.Value) ? Guid.Empty : Guid.Parse(hashClaim.Value);
        Claim? userClaim = httpContextAccessor.HttpContext?.User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Name);
        _userCode = userClaim?.Value!;
    }

    private string EntityName => typeof(T).Name;
    private RedisKey EntityCounterKey => $"{EntityName}:Counter";

    public async Task<Result<T>> Save(T entity)
    {
        if (!string.IsNullOrEmpty(entity.Id))
        {
            return new Result<T>()
                .SetReturnType(ReturnType.InvalidOperation)
                .SetDescription("Invalid request, this method accepts only insert. Do not set id for this method.")
                .SetData(entity)
                .SetTraceId(_traceId);
        }

        if (entity is IVersionAbleEntity versionAbleEntity)
        {
            versionAbleEntity.Version += 1;
        }

        string id = await _redisCollection.InsertAsync(entity);
        await IncrementEntityCount();
        entity.Id = id;
        return new Result<T>()
            .SetReturnType(ReturnType.Success)
            .SetData(entity)
            .SetTraceId(_traceId);
    }

    public async Task<Result<T?>> FindById(string id)
    {
        T? entity = await _redisCollection.FindByIdAsync(id);
        Result<T?> result = new Result<T?>()
            .SetData(entity)
            .SetReturnType(entity == null ? ReturnType.EntityIsNull : ReturnType.Success)
            .SetTraceId(_traceId);
        return result;
    }

    public Result<IList<T>> Get(Func<T, bool> predicate)
    {
        IList<T> entities = _redisCollection.Where(predicate).ToList();
        int countValue = entities.Count;
        Result<IList<T>> result = new Result<IList<T>>()
            .SetData(entities)
            .SetTraceId(_traceId)
            .SetPagination(new Pagination(0, 0, countValue))
            .SetReturnType(countValue > 0 ? ReturnType.Success : ReturnType.CollectionIsEmpty);
        return result;
    }

    public async Task<Result<IList<T>>> Get()
    {
        long countValue = await GetEntityCount();
        if (countValue > MaxEntityCount)
        {
            return new Result<IList<T>>()
                .SetTraceId(_traceId)
                .SetPagination(new Pagination(0, 0, countValue))
                .SetDescription("Too many record. Use pagination method.")
                .SetReturnType(ReturnType.TooManyRecords);
        }

        IList<T> entities = await _redisCollection.ToListAsync();
        Result<IList<T>> result = new Result<IList<T>>()
            .SetData(entities)
            .SetTraceId(_traceId)
            .SetReturnType(entities.Count > 0 ? ReturnType.Success : ReturnType.CollectionIsEmpty);
        return result;
    }

    public async Task<Result<T>> Update(T entity)
    {
        T? existingEntity = await _redisCollection.FindByIdAsync(entity.Id);
        if (existingEntity is null)
        {
            return new Result<T>()
                .SetData(entity)
                .SetTraceId(_traceId)
                .SetDescription("Entity does not exist.")
                .SetReturnType(ReturnType.NotFound);
        }

        if (entity is IVersionAbleEntity versionAbleEntity &&
            existingEntity is IVersionAbleEntity versionAbleExistingEntity)
        {
            if (versionAbleEntity.Version != versionAbleExistingEntity.Version)
                return new Result<T>()
                    .SetData(entity)
                    .SetTraceId(_traceId)
                    .SetDescription("Entity version does not match. Fetch entity again.")
                    .SetReturnType(ReturnType.InvalidVersion);

            versionAbleEntity.Version++;
        }

        await SaveHistory(existingEntity, entity);

        await _redisCollection.UpdateAsync(entity);

        return new Result<T>()
            .SetData(entity)
            .SetTraceId(_traceId)
            .SetReturnType(ReturnType.Success);
    }

    public async Task<Result<T?>> Delete(string id)
    {
        T? entity = await _redisCollection.FindByIdAsync(id);
        if (entity is null)
        {
            return new Result<T?>()
                .SetData(entity)
                .SetTraceId(_traceId)
                .SetDescription("Entity not found")
                .SetReturnType(ReturnType.NotFound);
        }

        await _redisCollection.DeleteAsync(entity);
        await DecrementEntityCount();
        return new Result<T?>()
            .SetData(entity)
            .SetTraceId(_traceId)
            .SetReturnType(ReturnType.Success);
    }

    public async Task<Result<IList<T>>> Get(int offset, int limit)
    {
        if (limit > MaxEntityCount)
            limit = MaxEntityCount;
        if (offset < 0)
            offset = 0;

        var entities = await _redisCollection.Skip(offset * limit).Take(limit).ToListAsync();
        long countValue = await GetEntityCount();
        Result<IList<T>> result = new Result<IList<T>>()
            .SetData(entities)
            .SetTraceId(_traceId)
            .SetPagination(new Pagination(0, 0, countValue))
            .SetReturnType(ReturnType.Success);
        return result;
    }

    public Result<IList<T>> Get(Func<T, bool> predicate, int offset, int limit)
    {
        if (limit > MaxEntityCount)
            limit = MaxEntityCount;
        if (offset < 0)
            offset = 0;

        IList<T> filteredEntities = _redisCollection.Where(predicate).ToList();
        long countValue = filteredEntities.Count;
        IList<T> entities = filteredEntities.Skip(offset * limit).Take(limit).ToList();
        Result<IList<T>> result = new Result<IList<T>>()
            .SetData(entities)
            .SetPagination(new Pagination(0, 0, countValue))
            .SetTraceId(_traceId)
            .SetReturnType(entities.Count > 0 ? ReturnType.Success : ReturnType.CollectionIsEmpty);
        return result;
    }

    public async Task<Result<History>> GetHistory(string id)
    {
        RedisKey streamKey = $"{EntityName}:{id}:history";
        StreamEntry[]? streamEntries = await _database.StreamRangeAsync(streamKey, "-", "+");

        var entHist = new History
        {
            id = id,
            entity_name = EntityName,
            records = new List<HistoryRecord>()
        };
        foreach (StreamEntry streamEntry in streamEntries)
        {
            var histRecord = new HistoryRecord();
            string streamId = streamEntry.Id.ToString();
            histRecord.stream_id = streamId;
            double ticks = double.Parse(streamId.Split("-")[0]);
            TimeSpan time = TimeSpan.FromMilliseconds(ticks);
            histRecord.date = new DateTime(1970, 1, 1) + time;
            histRecord.records = new List<Record>();
            foreach (NameValueEntry entry in streamEntry.Values)
            {
                histRecord.records.Add(entry);
            }

            entHist.records.Add(histRecord);
        }

        return new Result<History>()
            .SetData(entHist)
            .SetTraceId(_traceId)
            .SetReturnType(ReturnType.Success);
    }

    private async Task SaveHistory(T oldEntity, T newEntity)
    {
        RedisKey streamKey = $"{EntityName}:{oldEntity.Id}:history";
        IList<KeyValuePair<string, string>> changes = newEntity.GetChanges(oldEntity);
        if (changes.Any())
        {
            changes.Add(new KeyValuePair<string, string>("user", _userCode));
            NameValueEntry[] nameValueEntries = changes.Select(s => new NameValueEntry(s.Key, s.Value)).ToArray();
            await _database.StreamAddAsync(streamKey, nameValueEntries, maxLength: 10, useApproximateMaxLength: true);
        }
    }

    private async Task IncrementEntityCount()
    {
        await _database.StringIncrementAsync(EntityCounterKey);
    }

    private async Task DecrementEntityCount()
    {
        await _database.StringDecrementAsync(EntityCounterKey);
    }

    private async Task<long> GetEntityCount()
    {
        string count = await _database.StringGetAsync(EntityCounterKey);
        return long.Parse(count);
    }
}