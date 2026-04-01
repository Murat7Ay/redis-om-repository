using System.Linq.Expressions;
using CrudApp.Entity;
using CrudApp.Models;
using CrudApp.Specification;

namespace CrudApp.Repository;

public interface IRepository<T> where T : class, IEntity<T>, new()
{
    Task<Result<T>> AddAsync(T entity, CancellationToken cancellationToken = default);
    Task<Result<T>> UpdateAsync(T entity, CancellationToken cancellationToken = default);
    Task<Result<T?>> DeleteAsync(string id, CancellationToken cancellationToken = default);
    Task<Result<T?>> PurgeAsync(string id, CancellationToken cancellationToken = default);
    Task<Result<T?>> RestoreAsync(string id, CancellationToken cancellationToken = default);
    Task<Result<T?>> FindByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<Result<T?>> FindOneAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default);
    Task<Result<T?>> FindOneAsync(ISpecification<T> specification, CancellationToken cancellationToken = default);
    Task<Result<IList<T>>> ListAsync(CancellationToken cancellationToken = default);
    Task<Result<IList<T>>> ListAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default);
    Task<Result<IList<T>>> ListAsync(ISpecification<T> specification, CancellationToken cancellationToken = default);
    Task<Result<IList<T>>> PageAsync(int offset, int limit, CancellationToken cancellationToken = default);
    Task<Result<IList<T>>> PageAsync(Expression<Func<T, bool>> predicate, int offset, int limit, CancellationToken cancellationToken = default);
    Task<Result<IList<T>>> PageAsync(ISpecification<T> specification, int offset, int limit, CancellationToken cancellationToken = default);
    Task<Result<History>> GetHistoryAsync(string id, CancellationToken cancellationToken = default);
}
