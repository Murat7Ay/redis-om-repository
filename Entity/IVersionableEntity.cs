namespace CrudApp.Entity;

public interface IVersionable
{
    int RowVersion { get; set; }
}
