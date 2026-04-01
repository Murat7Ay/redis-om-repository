namespace CrudApp.ChangeTracking;

[AttributeUsage(AttributeTargets.Property)]
public sealed class SensitivePropertyAttribute : Attribute;
