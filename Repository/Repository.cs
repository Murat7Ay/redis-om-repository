using System.Linq.Expressions;
using System.Security.Claims;
using CrudApp.Entity;
using CrudApp.Enums;
using CrudApp.Models;
using CrudApp.Settings;
using CrudApp.Specification;
using Microsoft.Extensions.Options;
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
    private readonly int _maxEntityCount;
    private readonly int _historyMaxLength;
    private readonly bool _isSoftDeletable = typeof(ISoftDeletable).IsAssignableFrom(typeof(T));

    public Repository(
        RedisConnectionProvider provider,
        IDatabase database,
        IHttpContextAccessor httpContextAccessor,
        IOptions<ApiSettings> apiOptions)
    {
        _database = database;
        _redisCollection = provider.RedisCollection<T>();

        var settings = apiOptions.Value;
        _maxEntityCount = settings.MaxEntityCount;
        _historyMaxLength = settings.HistoryMaxLength;

        Claim? hashClaim = httpContextAccessor.HttpContext?.User.Claims
            .FirstOrDefault(c => c.Type == ClaimTypes.Hash);
        _traceId = Guid.TryParse(hashClaim?.Value, out Guid parsedTraceId)
            ? parsedTraceId
            : Guid.Empty;

        Claim? userClaim = httpContextAccessor.HttpContext?.User.Claims
            .FirstOrDefault(c => c.Type == ClaimTypes.Name);
        _userCode = string.IsNullOrWhiteSpace(userClaim?.Value) ? "system" : userClaim.Value;
    }

    private string EntityName => typeof(T).Name;

    private (int Offset, int Limit) NormalizePagination(int offset, int limit)
    {
        if (limit <= 0 || limit > _maxEntityCount)
            limit = _maxEntityCount;
        if (offset < 0)
            offset = 0;
        return (offset, limit);
    }

    private void SetAuditOnCreate(T entity)
    {
        if (entity is IAuditableEntity auditable)
        {
            auditable.CreatedAt = DateTime.UtcNow;
            auditable.UpdatedAt = DateTime.UtcNow;
            auditable.CreatedBy = _userCode;
            auditable.UpdatedBy = _userCode;
        }

        if (entity is IVersionable versionable)
            versionable.RowVersion = 1;

        if (entity is ISoftDeletable softDeletable)
            softDeletable.IsDeleted = false;
    }

    private void SetAuditOnUpdate(T entity)
    {
        if (entity is IAuditableEntity auditable)
        {
            auditable.UpdatedAt = DateTime.UtcNow;
            auditable.UpdatedBy = _userCode;
        }
    }

    private IRedisCollection<T> ActiveRecords()
    {
        if (_isSoftDeletable)
            return _redisCollection.Where(e => ((ISoftDeletable)(object)e).IsDeleted == false);
        return _redisCollection;
    }

    private long CountActive() => ActiveRecords().Count();

    private long CountActive(Expression<Func<T, bool>> predicate)
    {
        if (_isSoftDeletable)
            return _redisCollection.Where(predicate)
                .Where(e => ((ISoftDeletable)(object)e).IsDeleted == false).Count();
        return _redisCollection.Where(predicate).Count();
    }

    private long CountActive(ISpecification<T> specification)
    {
        if (_isSoftDeletable)
            return _redisCollection.Where(specification.Criteria)
                .Where(e => ((ISoftDeletable)(object)e).IsDeleted == false).Count();
        return _redisCollection.Where(specification.Criteria).Count();
    }

    private IRedisCollection<T> ApplySpecification(ISpecification<T> specification)
    {
        var query = _redisCollection.Where(specification.Criteria);
        if (_isSoftDeletable)
            query = query.Where(e => ((ISoftDeletable)(object)e).IsDeleted == false);
        return query;
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

        SetAuditOnCreate(entity);

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

        if (entity is ISoftDeletable { IsDeleted: true })
            entity = null;

        return new Result<T?>()
            .SetData(entity)
            .SetReturnType(entity == null ? ReturnType.EntityIsNull : ReturnType.Success)
            .SetTraceId(_traceId);
    }

    public async Task<Result<T?>> FindOneAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
    {
        var query = _redisCollection.Where(predicate);
        if (_isSoftDeletable)
            query = query.Where(e => ((ISoftDeletable)(object)e).IsDeleted == false);

        IList<T> entities = await query.Take(1).ToListAsync();
        T? entity = entities.FirstOrDefault();
        return new Result<T?>()
            .SetData(entity)
            .SetTraceId(_traceId)
            .SetReturnType(entity == null ? ReturnType.EntityIsNull : ReturnType.Success);
    }

    public async Task<Result<T?>> FindOneAsync(ISpecification<T> specification, CancellationToken cancellationToken = default)
    {
        IList<T> entities = await ApplySpecification(specification).Take(1).ToListAsync();
        T? entity = entities.FirstOrDefault();
        return new Result<T?>()
            .SetData(entity)
            .SetTraceId(_traceId)
            .SetReturnType(entity == null ? ReturnType.EntityIsNull : ReturnType.Success);
    }

    public async Task<Result<IList<T>>> ListAsync(CancellationToken cancellationToken = default)
    {
        long countValue = CountActive();
        if (countValue > _maxEntityCount)
        {
            return new Result<IList<T>>()
                .SetTraceId(_traceId)
                .SetPagination(new Pagination(_maxEntityCount, 0, countValue))
                .SetDescription("Too many records. Use pagination method.")
                .SetReturnType(ReturnType.TooManyRecords);
        }

        IList<T> entities = await ActiveRecords().ToListAsync();
        return new Result<IList<T>>()
            .SetData(entities)
            .SetTraceId(_traceId)
            .SetPagination(new Pagination(entities.Count, 0, countValue))
            .SetReturnType(entities.Count > 0 ? ReturnType.Success : ReturnType.CollectionIsEmpty);
    }

    public async Task<Result<IList<T>>> ListAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
    {
        long countValue = CountActive(predicate);
        if (countValue > _maxEntityCount)
        {
            return new Result<IList<T>>()
                .SetTraceId(_traceId)
                .SetPagination(new Pagination(_maxEntityCount, 0, countValue))
                .SetDescription("Too many records. Use pagination method.")
                .SetReturnType(ReturnType.TooManyRecords);
        }

        var query = _redisCollection.Where(predicate);
        if (_isSoftDeletable)
            query = query.Where(e => ((ISoftDeletable)(object)e).IsDeleted == false);

        IList<T> entities = await query.ToListAsync();
        return new Result<IList<T>>()
            .SetData(entities)
            .SetTraceId(_traceId)
            .SetPagination(new Pagination(entities.Count, 0, countValue))
            .SetReturnType(entities.Count > 0 ? ReturnType.Success : ReturnType.CollectionIsEmpty);
    }

    public async Task<Result<IList<T>>> ListAsync(ISpecification<T> specification, CancellationToken cancellationToken = default)
    {
        long countValue = CountActive(specification);
        if (countValue > _maxEntityCount)
        {
            return new Result<IList<T>>()
                .SetTraceId(_traceId)
                .SetPagination(new Pagination(_maxEntityCount, 0, countValue))
                .SetDescription("Too many records. Use pagination method.")
                .SetReturnType(ReturnType.TooManyRecords);
        }

        IList<T> entities = await ApplySpecification(specification).ToListAsync();
        return new Result<IList<T>>()
            .SetData(entities)
            .SetTraceId(_traceId)
            .SetPagination(new Pagination(entities.Count, 0, countValue))
            .SetReturnType(entities.Count > 0 ? ReturnType.Success : ReturnType.CollectionIsEmpty);
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

        if (existingEntity is ISoftDeletable { IsDeleted: true })
        {
            return new Result<T>()
                .SetData(entity)
                .SetTraceId(_traceId)
                .SetDescription("Cannot update a deleted entity. Restore it first.")
                .SetReturnType(ReturnType.InvalidOperation);
        }

        if (entity is IVersionable versionable &&
            existingEntity is IVersionable existingVersionable)
        {
            if (versionable.RowVersion != existingVersionable.RowVersion)
                return new Result<T>()
                    .SetData(entity)
                    .SetTraceId(_traceId)
                    .SetDescription("Entity version does not match. Fetch entity again.")
                    .SetReturnType(ReturnType.InvalidVersion);

            versionable.RowVersion++;
        }

        SetAuditOnUpdate(entity);
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
                .SetData(default)
                .SetTraceId(_traceId)
                .SetDescription("Entity not found")
                .SetReturnType(ReturnType.NotFound);
        }

        if (entity is ISoftDeletable softDeletable)
        {
            if (softDeletable.IsDeleted)
            {
                return new Result<T?>()
                    .SetData(entity)
                    .SetTraceId(_traceId)
                    .SetDescription("Entity is already deleted.")
                    .SetReturnType(ReturnType.InvalidOperation);
            }

            softDeletable.IsDeleted = true;
            softDeletable.DeletedAt = DateTime.UtcNow;
            softDeletable.DeletedBy = _userCode;

            if (entity is IVersionable versionable)
                versionable.RowVersion++;

            SetAuditOnUpdate(entity);
            await _redisCollection.UpdateAsync(entity);

            return new Result<T?>()
                .SetData(entity)
                .SetTraceId(_traceId)
                .SetReturnType(ReturnType.Success);
        }

        await _redisCollection.DeleteAsync(entity);
        return new Result<T?>()
            .SetData(entity)
            .SetTraceId(_traceId)
            .SetReturnType(ReturnType.Success);
    }

    public async Task<Result<T?>> PurgeAsync(string id, CancellationToken cancellationToken = default)
    {
        T? entity = await _redisCollection.FindByIdAsync(id);
        if (entity is null)
        {
            return new Result<T?>()
                .SetData(default)
                .SetTraceId(_traceId)
                .SetDescription("Entity not found")
                .SetReturnType(ReturnType.NotFound);
        }

        await _redisCollection.DeleteAsync(entity);
        return new Result<T?>()
            .SetData(entity)
            .SetTraceId(_traceId)
            .SetDescription("Entity permanently deleted.")
            .SetReturnType(ReturnType.Success);
    }

    public async Task<Result<T?>> RestoreAsync(string id, CancellationToken cancellationToken = default)
    {
        T? entity = await _redisCollection.FindByIdAsync(id);
        if (entity is null)
        {
            return new Result<T?>()
                .SetData(default)
                .SetTraceId(_traceId)
                .SetDescription("Entity not found")
                .SetReturnType(ReturnType.NotFound);
        }

        if (entity is not ISoftDeletable softDeletable)
        {
            return new Result<T?>()
                .SetData(entity)
                .SetTraceId(_traceId)
                .SetDescription("Entity does not support soft-delete.")
                .SetReturnType(ReturnType.InvalidOperation);
        }

        if (!softDeletable.IsDeleted)
        {
            return new Result<T?>()
                .SetData(entity)
                .SetTraceId(_traceId)
                .SetDescription("Entity is not deleted.")
                .SetReturnType(ReturnType.InvalidOperation);
        }

        softDeletable.IsDeleted = false;
        softDeletable.DeletedAt = null;
        softDeletable.DeletedBy = null;

        if (entity is IVersionable versionable)
            versionable.RowVersion++;

        SetAuditOnUpdate(entity);
        await _redisCollection.UpdateAsync(entity);

        return new Result<T?>()
            .SetData(entity)
            .SetTraceId(_traceId)
            .SetDescription("Entity restored.")
            .SetReturnType(ReturnType.Success);
    }

    public async Task<Result<IList<T>>> PageAsync(int offset, int limit, CancellationToken cancellationToken = default)
    {
        (int normalizedOffset, int normalizedLimit) = NormalizePagination(offset, limit);

        var entities = await ActiveRecords().Skip(normalizedOffset).Take(normalizedLimit).ToListAsync();
        long countValue = CountActive();
        return new Result<IList<T>>()
            .SetData(entities)
            .SetTraceId(_traceId)
            .SetPagination(new Pagination(normalizedLimit, normalizedOffset, countValue))
            .SetReturnType(ReturnType.Success);
    }

    public async Task<Result<IList<T>>> PageAsync(Expression<Func<T, bool>> predicate, int offset, int limit, CancellationToken cancellationToken = default)
    {
        (int normalizedOffset, int normalizedLimit) = NormalizePagination(offset, limit);

        var query = _redisCollection.Where(predicate);
        if (_isSoftDeletable)
            query = query.Where(e => ((ISoftDeletable)(object)e).IsDeleted == false);

        long countValue = query.Count();
        IList<T> entities = await query.Skip(normalizedOffset).Take(normalizedLimit).ToListAsync();
        return new Result<IList<T>>()
            .SetData(entities)
            .SetPagination(new Pagination(normalizedLimit, normalizedOffset, countValue))
            .SetTraceId(_traceId)
            .SetReturnType(entities.Count > 0 ? ReturnType.Success : ReturnType.CollectionIsEmpty);
    }

    public async Task<Result<IList<T>>> PageAsync(ISpecification<T> specification, int offset, int limit, CancellationToken cancellationToken = default)
    {
        (int normalizedOffset, int normalizedLimit) = NormalizePagination(offset, limit);

        var query = ApplySpecification(specification);
        long countValue = query.Count();
        IList<T> entities = await query.Skip(normalizedOffset).Take(normalizedLimit).ToListAsync();
        return new Result<IList<T>>()
            .SetData(entities)
            .SetPagination(new Pagination(normalizedLimit, normalizedOffset, countValue))
            .SetTraceId(_traceId)
            .SetReturnType(entities.Count > 0 ? ReturnType.Success : ReturnType.CollectionIsEmpty);
    }

    public async Task<Result<History>> GetHistoryAsync(string id, CancellationToken cancellationToken = default)
    {
        RedisKey streamKey = $"{EntityName}:{id}:history";
        StreamEntry[]? streamEntries = await _database.StreamRangeAsync(streamKey, "-", "+");

        var history = new History
        {
            Id = id,
            EntityName = EntityName,
            Records = new List<HistoryRecord>()
        };

        foreach (StreamEntry streamEntry in streamEntries)
        {
            string streamId = streamEntry.Id.ToString();
            string ticksPart = streamId.Split("-")[0];
            DateTime date = long.TryParse(ticksPart, out long ticks)
                ? DateTimeOffset.FromUnixTimeMilliseconds(ticks).UtcDateTime
                : DateTime.MinValue;

            var histRecord = new HistoryRecord
            {
                StreamId = streamId,
                Date = date,
                Records = streamEntry.Values.Select(e => (ChangeRecord)e).ToList()
            };

            history.Records.Add(histRecord);
        }

        return new Result<History>()
            .SetData(history)
            .SetTraceId(_traceId)
            .SetReturnType(ReturnType.Success);
    }

    private async Task SaveHistory(T oldEntity, T newEntity)
    {
        RedisKey streamKey = $"{EntityName}:{oldEntity.Id}:history";
        IEntity<T> tracked = newEntity;
        IList<KeyValuePair<string, string>> changes = tracked.GetChanges(oldEntity);

        if (changes.Count > 0)
        {
            changes.Add(new KeyValuePair<string, string>("user", _userCode));
            NameValueEntry[] nameValueEntries = changes
                .Select(s => new NameValueEntry(s.Key, s.Value))
                .ToArray();
            await _database.StreamAddAsync(
                streamKey,
                nameValueEntries,
                maxLength: _historyMaxLength,
                useApproximateMaxLength: true);
        }
    }
}
