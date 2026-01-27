using CrudApp.Entity;

namespace CrudApp.Specification;

public sealed class RoseSearchSpec : Specification<RoseEntity>
{
    public RoseSearchSpec(string query)
        : base(rose => rose.Name.Contains(query) || rose.Description.Contains(query))
    {
    }
}
