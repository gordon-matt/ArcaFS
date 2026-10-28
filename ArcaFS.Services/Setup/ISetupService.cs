namespace ArcaFS.Services.Setup;

/// <summary>
/// Drives the first-run wizard. Every method here is reachable without authentication while
/// setup is incomplete, and must refuse to do anything once it is — otherwise the wizard would
/// be a permanent back door for creating administrators.
/// </summary>
public interface ISetupService
{
    Task<SetupStateDto> GetStateAsync(CancellationToken cancellationToken = default);

    /// <summary>Creates the first administrator. Only valid while no administrator exists.</summary>
    Task<Result> CreateAdministratorAsync(
        SetupAdminRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Creates the first source, optionally with the per-user home directory template.</summary>
    Task<Result<SourceDto>> CreateFirstSourceAsync(
        SetupSourceRequest request,
        CancellationToken cancellationToken = default);

    Task<Result> CompleteAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Server-side directory browser for choosing a source path. Restricted to the setup
    /// window and to administrators afterwards. When <c>Hosting:DataRoot</c> exists, browsing
    /// cannot leave that directory.
    /// </summary>
    Task<Result<HostDirectoryListingDto>> BrowseHostDirectoriesAsync(
        string? path,
        CancellationToken cancellationToken = default);
}