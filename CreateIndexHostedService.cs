using CrudApp.Entity;
using Redis.OM;
using Redis.OM.Contracts;

namespace CrudApp;

public class CreateIndexHostedService : IHostedService
{
    private readonly RedisConnectionProvider _provider;

    public CreateIndexHostedService(RedisConnectionProvider provider)
    {
        _provider = provider;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var entityTypes = System.Reflection.Assembly.GetExecutingAssembly().GetTypes()
            .Where(mytype => mytype.IsClass && mytype.GetInterfaces().Contains(typeof(IEntityId)));
        IRedisConnection connection = _provider.Connection;
        foreach (Type entity in entityTypes)
        {
            await connection.CreateIndexAsync(entity);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}