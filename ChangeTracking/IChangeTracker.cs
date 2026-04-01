namespace CrudApp.ChangeTracking;

public interface IChangeTracker<in T> where T : class
{
    IList<KeyValuePair<string, string>> DetectChanges(T current, T previous);
}
