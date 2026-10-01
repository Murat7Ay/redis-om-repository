using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Redis.OM;
using Redis.OM.Modeling;

namespace RedisCrud.Persistence;

/// <summary>
/// The serializer the store uses for documents: Redis OM's options (same property names and
/// converters, so the Query Engine index and Redis OM reads stay compatible) with the DateTime
/// converter replaced. Redis OM's converter interprets <see cref="DateTimeKind.Unspecified"/> values
/// in the host's local time zone, which shifts stored values per server and throws for
/// <see cref="DateTime.MinValue"/> on UTC+ hosts.
/// </summary>
public static class StorageJson
{
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(RedisSerializationSettings.JsonSerializerOptions);
        for (int i = options.Converters.Count - 1; i >= 0; i--)
        {
            if (options.Converters[i] is DateTimeJsonConverter)
                options.Converters.RemoveAt(i);
        }
        options.Converters.Insert(0, new UtcUnixMillisecondsConverter());
        options.TypeInfoResolver ??= new DefaultJsonTypeInfoResolver();
        options.MakeReadOnly();
        return options;
    }

    /// <summary>Unix milliseconds, the format Redis OM indexes as NUMERIC. Unspecified kinds are taken as UTC.</summary>
    private sealed class UtcUnixMillisecondsConverter : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType == JsonTokenType.Number
                ? DateTime.UnixEpoch.AddMilliseconds(reader.GetInt64())
                : DateTime.SpecifyKind(reader.GetDateTime().ToUniversalTime(), DateTimeKind.Utc);

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        {
            var utc = value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc);
            writer.WriteNumberValue((utc.Ticks - DateTime.UnixEpoch.Ticks) / TimeSpan.TicksPerMillisecond);
        }
    }
}
