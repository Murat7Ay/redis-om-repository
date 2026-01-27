using System.Linq.Expressions;

namespace CrudApp.Specification;

public interface ISpecification<T>
{
    Expression<Func<T, bool>> Criteria { get; }
}
