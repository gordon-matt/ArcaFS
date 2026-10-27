using Heroicsoft.NetForge.Abstractions.Setup;

namespace ArcaFS.Infrastructure;

/// <summary>Feeds the library's setup gate from the persisted first-run flag.</summary>
public sealed class ServerSetupStatus(IServerSettingsService settingsService) : ISetupStatus
{
    public Task<bool> IsCompleteAsync(CancellationToken cancellationToken = default) =>
        settingsService.IsSetupCompleteAsync(cancellationToken);
}