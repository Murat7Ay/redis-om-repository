using System.Linq.Expressions;

namespace CrudApp.Specification;

public abstract class Specification<T> : ISpecification<T>
{
    protected Specification(Expression<Func<T, bool>> criteria)
    {
        Criteria = criteria ?? throw new ArgumentNullException(nameof(criteria));
    }

    public Expression<Func<T, bool>> Criteria { get; }
}
