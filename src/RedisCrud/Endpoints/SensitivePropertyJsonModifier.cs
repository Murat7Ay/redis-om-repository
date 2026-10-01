using System.Text.Json.Serialization.Metadata;

namespace RedisCrud.Endpoints;

/// <summary>
/// Makes <see cref="SensitivePropertyAttribute"/> properties write-only in HTTP JSON:
/// they bind from request bodies but are never serialized into responses.
/// Applied to the HTTP serializer only; storage serialization is unaffected.
/// </summary>
internal static class SensitivePropertyJsonModifier
{
    public static void Apply(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Kind != JsonTypeInfoKind.Object || !typeof(Entity).IsAssignableFrom(typeInfo.Type))
            return;

        foreach (var property in typeInfo.Properties)
        {
            if (property.AttributeProvider?.IsDefined(typeof(SensitivePropertyAttribute), inherit: true) == true)
                property.ShouldSerialize = static (_, _) => false;
        }
    }
}
