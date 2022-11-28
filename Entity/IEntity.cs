namespace CrudApp.Entity;

public interface IEntity<in T> : IEntityId where T : class
{
    public IList<KeyValuePair<string, string>> GetChanges(T oldOne);
}