using CrudApp.ChangeTracking;

namespace CrudApp.Entity;

public interface IEntity<T> : IEntityId where T : class
{
    IList<KeyValuePair<string, string>> GetChanges(T oldOne)
    {
        var tracker = new ReflectionChangeTracker<T>();
        return tracker.DetectChanges((T)this, oldOne);
    }
}
