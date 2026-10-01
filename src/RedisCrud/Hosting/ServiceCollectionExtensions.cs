using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Configuration;
using RedisCrud.Endpoints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Redis.OM;
using RedisCrud.Persistence;
using StackExchange.Redis;

namespace RedisCrud.Hosting;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Redis connection, <see cref="RedisEntityStore{T}"/> for every entity type,
    /// and index provisioning for the entity types passed to <see cref="CrudBuilder.AddEntity{T}"/>
    /// or mapped with <c>MapCrud</c>.
    /// </summary>
    public static CrudBuilder AddRedisCrud(this IServiceCollection services, IConfiguration section)
    {
        // Writes use StorageJson (time-zone independent). This makes Redis OM's own reads (store.Query())
        // return UTC values too, instead of converting stored timestamps to the host's local time.
        RedisSerializationSettings.UseUtcTime();

        services.AddOptions<CrudOptions>()
            .Bind(section)
            .Validate(o => o.MaxPageSize is > 0 and <= 1000 && o.DefaultPageSize > 0 && o.DefaultPageSize <= o.MaxPageSize,
                "Crud: page sizes must satisfy 0 < DefaultPageSize <= MaxPageSize <= 1000.")
            .Validate(o => o.HistoryMaxLength >= 0, "Crud: HistoryMaxLength must be >= 0.")
            .ValidateOnStart();

        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var options = ConfigurationOptions.Parse(sp.GetRequiredService<IOptions<CrudOptions>>().Value.ConnectionString);
            options.AbortOnConnectFail = false;
            return ConnectionMultiplexer.Connect(options);
        });
        services.AddSingleton(sp => new RedisConnectionProvider(sp.GetRequiredService<IConnectionMultiplexer>()));
        services.AddHttpContextAccessor();
        services.AddSingleton<ICrudActor, HttpContextCrudActor>();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped(typeof(RedisEntityStore<>));
        services.ConfigureHttpJsonOptions(o =>
            o.SerializerOptions.TypeInfoResolver = (o.SerializerOptions.TypeInfoResolver ?? new DefaultJsonTypeInfoResolver())
                .WithAddedModifier(SensitivePropertyJsonModifier.Apply));

        var registry = new EntityRegistry();
        services.AddSingleton(registry);
        services.AddHostedService<IndexProvisioningService>();

        return new CrudBuilder(services, registry);
    }
}

public sealed class CrudBuilder(IServiceCollection services, EntityRegistry registry)
{
    public IServiceCollection Services { get; } = services;

    /// <summary>Provision the index for an entity that is used through the store but not mapped to endpoints.</summary>
    public CrudBuilder AddEntity<T>() where T : Entity, new()
    {
        registry.Add<T>();
        return this;
    }
}

/// <summary>Entity types whose indexes are provisioned at startup.</summary>
public sealed class EntityRegistry
{
    private readonly HashSet<Type> _types = [];

    public IReadOnlyCollection<Type> Types => _types;

    public void Add<T>() where T : Entity, new()
    {
        // Touch metadata eagerly so a mis-declared entity fails at startup, not on first request.
        _ = EntityMetadata<T>.Instance;
        _types.Add(typeof(T));
    }
}
