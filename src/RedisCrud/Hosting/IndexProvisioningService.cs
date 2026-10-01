using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Redis.OM;

namespace RedisCrud.Hosting;

/// <summary>
/// Creates missing indexes and detects drift between entity attributes and the live index definition.
/// Runs before the server starts accepting requests.
/// </summary>
internal sealed class IndexProvisioningService(
    RedisConnectionProvider provider,
    EntityRegistry registry,
    IOptions<CrudOptions> options,
    ILogger<IndexProvisioningService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var connection = provider.Connection;
        foreach (Type type in registry.Types)
        {
            if (await connection.CreateIndexAsync(type))
            {
                logger.LogInformation("Created index for {Entity}", type.Name);
                continue;
            }

            if (await connection.IsIndexCurrentAsync(type))
                continue;

            if (!options.Value.RecreateStaleIndexes)
                throw new InvalidOperationException(
                    $"The index for {type.Name} does not match its attributes and Crud:RecreateStaleIndexes is false.");

            logger.LogWarning("Index for {Entity} is stale; recreating it. Documents are kept and re-indexed in the background", type.Name);
            await connection.DropIndexAsync(type);
            await connection.CreateIndexAsync(type);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
