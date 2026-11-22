namespace ArcaFS.Services.Setup;

/// <summary>
/// Drives the first-run wizard. Every method here is reachable without authentication while
/// setup is incomplete, and must refuse to do anything once it is — otherwise the wizard would
/// be a permanent back door for creating administrators.
/// </summary>
public interface ISetupService
{
    /// <summary>Where the wizard is: whether setup is finished, an administrator exists, and a source exists.</summary>
    /// <param name="cancellationToken">Cancels the lookups.</param>
    /// <returns>The wizard state, including the active authentication provider and database provider names.</returns>
    Task<SetupStateDto> GetStateAsync(CancellationToken cancellationToken = default);

    /// <summary>Creates the first administrator. Only valid while no administrator exists.</summary>
    /// <param name="request">User name, password, and optional email. The display name stored is the user name.</param>
    /// <param name="cancellationToken">Cancels the setup-window check. Creating the Identity user is not cancelled.</param>
    /// <returns>
    /// Success, or a failure when setup is finished, an administrator already exists, the
    /// provider is not Identity, or Identity rejects the account.
    /// </returns>
    Task<Result> CreateAdministratorAsync(
        SetupAdminRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Creates the first source, optionally with the per-user home directory template.</summary>
    /// <param name="request">Name, host path, read-only flag, and whether each user gets a private folder.</param>
    /// <param name="cancellationToken">Cancels the setup-window check. The insert is not cancelled.</param>
    /// <returns>
    /// The new source, or a failure when the caller is not allowed to use the wizard, the
    /// folder is missing, or the path is outside the configured data root. An administrator
    /// can still call this after setup.
    /// </returns>
    Task<Result<SourceDto>> CreateFirstSourceAsync(
        SetupSourceRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks setup complete. After setup, only an administrator can call this.
    /// </summary>
    /// <param name="cancellationToken">Cancels the setup-window check and the flag write.</param>
    /// <returns>Success, or forbidden when setup is already complete and the caller is not an administrator.</returns>
    Task<Result> CompleteAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Server-side directory browser for choosing a source path. Restricted to the setup
    /// window and to administrators afterwards. When <c>Hosting:DataRoot</c> exists, browsing
    /// cannot leave that directory.
    /// </summary>
    /// <param name="path">
    /// Directory to list. When a data root is enforced, <see langword="null"/> or empty lists
    /// that root. Otherwise <see langword="null"/> or empty returns the machine's roots and
    /// does not open a directory.
    /// </param>
    /// <param name="cancellationToken">Cancels the setup check and the directory listing.</param>
    /// <returns>The directory and its child folders, or a failure when the path cannot be listed.</returns>
    Task<Result<HostDirectoryListingDto>> BrowseHostDirectoriesAsync(
        string? path,
        CancellationToken cancellationToken = default);
}
