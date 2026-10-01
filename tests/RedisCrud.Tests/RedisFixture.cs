using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Redis.OM;
using RedisCrud.Hosting;
using RedisCrud.Persistence;
using StackExchange.Redis;

namespace RedisCrud.Tests;

/// <summary>
/// A real Redis 8 (JSON + Query Engine) per test run, via Testcontainers.
/// Set REDIS_TEST_CONNECTION to use an existing server instead (it must be disposable: tests drop indexes).
/// </summary>
public sealed class RedisFixture : IAsyncLifetime
{
    private IContainer? _container;

    public string ConnectionString { get; private set; } = string.Empty;
    public IConnectionMultiplexer Redis { get; private set; } = null!;
    public RedisConnectionProvider Provider { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        RedisSerializationSettings.UseUtcTime();

        ConnectionString = Environment.GetEnvironmentVariable("REDIS_TEST_CONNECTION") ?? string.Empty;
        if (ConnectionString.Length == 0)
        {
            _container = new ContainerBuilder("redis:8")
                .WithPortBinding(6379, assignRandomHostPort: true)
                .WithWaitStrategy(Wait.ForUnixContainer().UntilCommandIsCompleted("redis-cli", "ping"))
                .Build();
            await _container.StartAsync();
            ConnectionString = $"{_container.Hostname}:{_container.GetMappedPublicPort(6379)}";
        }

        Redis = await ConnectionMultiplexer.ConnectAsync(ConnectionString);
        Provider = new RedisConnectionProvider(Redis);
    }

    public async Task DisposeAsync()
    {
        Redis?.Dispose();
        if (_container is not null)
            await _container.DisposeAsync();
    }

    /// <summary>Drops the index and every document for <typeparamref name="T"/>, then recreates the index.</summary>
    public async Task ResetAsync<T>() where T : Entity
    {
        try { Provider.Connection.DropIndexAndAssociatedRecords(typeof(T)); }
        catch (Exception e) when (e.Message.Contains("not found", StringComparison.OrdinalIgnoreCase) || e.Message.Contains("Unknown", StringComparison.OrdinalIgnoreCase)) { }
        await Provider.Connection.CreateIndexAsync(typeof(T));
    }

    public RedisEntityStore<T> Store<T>(string actor = "tester", CrudOptions? options = null, TimeProvider? time = null) where T : Entity, new() =>
        new(Redis, Provider, new FixedActor(actor), Options.Create(options ?? new CrudOptions()), time ?? TimeProvider.System,
            NullLogger<RedisEntityStore<T>>.Instance);

    private sealed class FixedActor(string id) : ICrudActor
    {
        public string Id => id;
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class RedisCollection : ICollectionFixture<RedisFixture>
{
    public const string Name = "redis";
}
