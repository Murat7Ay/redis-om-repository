using System.Linq.Expressions;
using System.Reflection;
using Redis.OM.Modeling;

namespace CrudApp.Specification;

public sealed class SearchSpecification<T> : Specification<T> where T : class
{
    public SearchSpecification(string query)
        : base(BuildCriteria(query))
    {
    }

    private static Expression<Func<T, bool>> BuildCriteria(string query)
    {
        var searchableProperties = typeof(T)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(string)
                        && p.GetCustomAttribute<SearchableAttribute>() != null)
            .ToList();

        if (searchableProperties.Count == 0)
        {
            var indexedStringProps = typeof(T)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.PropertyType == typeof(string)
                            && p.GetCustomAttribute<IndexedAttribute>() != null
                            && p.Name != "Id" && p.Name != "Password")
                .ToList();
            searchableProperties = indexedStringProps;
        }

        if (searchableProperties.Count == 0)
            return _ => true;

        var parameter = Expression.Parameter(typeof(T), "e");
        Expression? combined = null;

        foreach (var prop in searchableProperties)
        {
            var propertyAccess = Expression.Property(parameter, prop);
            var containsMethod = typeof(string).GetMethod("Contains", [typeof(string)])!;
            var queryConstant = Expression.Constant(query);
            var containsCall = Expression.Call(propertyAccess, containsMethod, queryConstant);

            combined = combined == null
                ? containsCall
                : Expression.OrElse(combined, containsCall);
        }

        return Expression.Lambda<Func<T, bool>>(combined!, parameter);
    }
}
