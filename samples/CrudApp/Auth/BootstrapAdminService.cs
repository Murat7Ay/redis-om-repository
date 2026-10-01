using Microsoft.Extensions.Options;

namespace CrudApp.Auth;

/// <summary>Creates the configured root account once. The only way to obtain a root user without an existing root.</summary>
public sealed class BootstrapAdminService(IServiceProvider services, IOptions<AuthOptions> options, ILogger<BootstrapAdminService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (options.Value.BootstrapAdmin is not { Name.Length: > 0, Password.Length: > 0 } admin)
            return;

        await using var scope = services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<UserAccounts>();
        if (await accounts.FindByNameAsync(admin.Name) is not null)
            return;

        if (await accounts.RegisterAsync(admin.Name, admin.Password, Roles.Root) is not null)
            logger.LogWarning("Created bootstrap root user {Name}. Remove Auth:BootstrapAdmin from configuration once it exists", admin.Name);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
