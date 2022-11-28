using CrudApp.Entity;
using CrudApp.Models;

namespace CrudApp.Repository;

public interface IRepository<T> where T : class, IEntity<T>, new()
{
    Task<Result<T>> Save(T entity);
    Task<Result<T?>> FindById(string id);
    Result<IList<T>> Get(Func<T, bool> predicate);
    Task<Result<IList<T>>> Get();
    Task<Result<T>> Update(T entity);
    Task<Result<T?>> Delete(string id);
    Task<Result<IList<T>>> Get(int offset, int limit);
    Result<IList<T>> Get(Func<T, bool> predicate, int offset, int limit);
    Task<Result<History>> GetHistory(string id);
}