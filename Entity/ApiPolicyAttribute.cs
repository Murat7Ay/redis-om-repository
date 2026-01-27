namespace CrudApp.Entity;

[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class ApiPolicyAttribute : Attribute
{
    public ApiPolicyAttribute(string? policy = null)
    {
        Policy = policy;
    }

    public string? Policy { get; }
}
