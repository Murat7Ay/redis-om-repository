namespace CrudApp.Entity;

public interface IDto<T> where T : class, IEntity<T>, new()
{
}