using System.Linq.Expressions;
using System.Security.Claims;
using CrudApp.Entity;
using CrudApp.Enums;
using CrudApp.Models;
using CrudApp.Specification;
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
        _traceId = Guid.TryParse(hashClaim?.Value, out Guid parsedTraceId) ? parsedTraceId : Guid.Empty;
        Claim? userClaim = httpContextAccessor.HttpContext?.User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Name);
        _userCode = string.IsNullOrWhiteSpace(userClaim?.Value) ? "system" : userClaim.Value;
    }

    private string EntityName => typeof(T).Name;
    private static (int Offset, int Limit) NormalizePagination(int offset, int limit)
    {
        if (limit <= 0 || limit > MaxEntityCount)
            limit = MaxEntityCount;
        if (offset < 0)
            offset = 0;
        return (offset, limit);
    }

    private long CountAll() => _redisCollection.Count();
    private long Count(Expression<Func<T, bool>> predicate) => _redisCollection.Where(predicate).Count();
    private long Count(ISpecification<T> specification) => _redisCollection.Where(specification.Criteria).Count();

    private IRedisCollection<T> ApplySpecification(ISpecification<T> specification)
    {
        return _redisCollection.Where(specification.Criteria);
    }

    public async Task<Result<T>> AddAsync(T entity, CancellationToken cancellationToken = default)
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
        entity.Id = id;
        return new Result<T>()
            .SetReturnType(ReturnType.Success)
            .SetData(entity)
            .SetTraceId(_traceId);
    }

    public async Task<Result<T?>> FindByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        T? entity = await _redisCollection.FindByIdAsync(id);
        Result<T?> result = new Result<T?>()
            .SetData(entity)
            .SetReturnType(entity == null ? ReturnType.EntityIsNull : ReturnType.Success)
            .SetTraceId(_traceId);
        return result;
    }

    public async Task<Result<T?>> FindOneAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
    {
        IList<T> entities = await _redisCollection.Where(predicate).Take(1).ToListAsync();
        T? entity = entities.FirstOrDefault();
        Result<T?> result = new Result<T?>()
            .SetData(entity)
            .SetTraceId(_traceId)
            .SetReturnType(entity == null ? ReturnType.EntityIsNull : ReturnType.Success);
        return result;
    }

    public async Task<Result<T?>> FindOneAsync(ISpecification<T> specification, CancellationToken cancellationToken = default)
    {
        IList<T> entities = await ApplySpecification(specification).Take(1).ToListAsync();
        T? entity = entities.FirstOrDefault();
        Result<T?> result = new Result<T?>()
            .SetData(entity)
            .SetTraceId(_traceId)
            .SetReturnType(entity == null ? ReturnType.EntityIsNull : ReturnType.Success);
        return result;
    }

    public async Task<Result<IList<T>>> ListAsync(CancellationToken cancellationToken = default)
    {
        long countValue = CountAll();
        if (countValue > MaxEntityCount)
        {
            return new Result<IList<T>>()
                .SetTraceId(_traceId)
                .SetPagination(new Pagination(MaxEntityCount, 0, countValue))
                .SetDescription("Too many record. Use pagination method.")
                .SetReturnType(ReturnType.TooManyRecords);
        }

        IList<T> entities = await _redisCollection.ToListAsync();
        Result<IList<T>> result = new Result<IList<T>>()
            .SetData(entities)
            .SetTraceId(_traceId)
            .SetPagination(new Pagination(entities.Count, 0, countValue))
            .SetReturnType(entities.Count > 0 ? ReturnType.Success : ReturnType.CollectionIsEmpty);
        return result;
    }

    public async Task<Result<IList<T>>> ListAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
    {
        long countValue = Count(predicate);
        if (countValue > MaxEntityCount)
        {
            return new Result<IList<T>>()
                .SetTraceId(_traceId)
                .SetPagination(new Pagination(MaxEntityCount, 0, countValue))
                .SetDescription("Too many record. Use pagination method.")
                .SetReturnType(ReturnType.TooManyRecords);
        }

        IList<T> entities = await _redisCollection.Where(predicate).ToListAsync();
        Result<IList<T>> result = new Result<IList<T>>()
            .SetData(entities)
            .SetTraceId(_traceId)
            .SetPagination(new Pagination(entities.Count, 0, countValue))
            .SetReturnType(entities.Count > 0 ? ReturnType.Success : ReturnType.CollectionIsEmpty);
        return result;
    }

    public async Task<Result<IList<T>>> ListAsync(ISpecification<T> specification, CancellationToken cancellationToken = default)
    {
        long countValue = Count(specification);
        if (countValue > MaxEntityCount)
        {
            return new Result<IList<T>>()
                .SetTraceId(_traceId)
                .SetPagination(new Pagination(MaxEntityCount, 0, countValue))
                .SetDescription("Too many record. Use pagination method.")
                .SetReturnType(ReturnType.TooManyRecords);
        }

        IList<T> entities = await ApplySpecification(specification).ToListAsync();
        Result<IList<T>> result = new Result<IList<T>>()
            .SetData(entities)
            .SetTraceId(_traceId)
            .SetPagination(new Pagination(entities.Count, 0, countValue))
            .SetReturnType(entities.Count > 0 ? ReturnType.Success : ReturnType.CollectionIsEmpty);
        return result;
    }

    public async Task<Result<T>> UpdateAsync(T entity, CancellationToken cancellationToken = default)
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

    public async Task<Result<T?>> DeleteAsync(string id, CancellationToken cancellationToken = default)
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
        return new Result<T?>()
            .SetData(entity)
            .SetTraceId(_traceId)
            .SetReturnType(ReturnType.Success);
    }

    public async Task<Result<IList<T>>> PageAsync(int offset, int limit, CancellationToken cancellationToken = default)
    {
        (int normalizedOffset, int normalizedLimit) = NormalizePagination(offset, limit);

        var entities = await _redisCollection.Skip(normalizedOffset).Take(normalizedLimit).ToListAsync();
        long countValue = CountAll();
        Result<IList<T>> result = new Result<IList<T>>()
            .SetData(entities)
            .SetTraceId(_traceId)
            .SetPagination(new Pagination(normalizedLimit, normalizedOffset, countValue))
            .SetReturnType(ReturnType.Success);
        return result;
    }

    public async Task<Result<IList<T>>> PageAsync(Expression<Func<T, bool>> predicate, int offset, int limit, CancellationToken cancellationToken = default)
    {
        (int normalizedOffset, int normalizedLimit) = NormalizePagination(offset, limit);

        var query = _redisCollection.Where(predicate);
        long countValue = query.Count();
        IList<T> entities = await query.Skip(normalizedOffset).Take(normalizedLimit).ToListAsync();
        Result<IList<T>> result = new Result<IList<T>>()
            .SetData(entities)
            .SetPagination(new Pagination(normalizedLimit, normalizedOffset, countValue))
            .SetTraceId(_traceId)
            .SetReturnType(entities.Count > 0 ? ReturnType.Success : ReturnType.CollectionIsEmpty);
        return result;
    }

    public async Task<Result<IList<T>>> PageAsync(ISpecification<T> specification, int offset, int limit, CancellationToken cancellationToken = default)
    {
        (int normalizedOffset, int normalizedLimit) = NormalizePagination(offset, limit);

        var query = ApplySpecification(specification);
        long countValue = query.Count();
        IList<T> entities = await query.Skip(normalizedOffset).Take(normalizedLimit).ToListAsync();
        Result<IList<T>> result = new Result<IList<T>>()
            .SetData(entities)
            .SetPagination(new Pagination(normalizedLimit, normalizedOffset, countValue))
            .SetTraceId(_traceId)
            .SetReturnType(entities.Count > 0 ? ReturnType.Success : ReturnType.CollectionIsEmpty);
        return result;
    }

    public async Task<Result<History>> GetHistoryAsync(string id, CancellationToken cancellationToken = default)
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
            string ticksPart = streamId.Split("-")[0];
            if (long.TryParse(ticksPart, out long ticks))
            {
                histRecord.date = DateTimeOffset.FromUnixTimeMilliseconds(ticks).UtcDateTime;
            }
            else
            {
                histRecord.date = DateTime.MinValue;
            }
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

}