namespace ArcaFS.Services.Sources;

/// <summary>
/// Administration of sources. Reading which sources a *user* can see is
/// <see cref="IPermissionService.GetAccessibleSourcesAsync"/>; everything here is administrator-only.
/// </summary>
public interface ISourceService
{
    Task<Result<IReadOnlyList<SourceDto>>> ListAsync(CancellationToken cancellationToken = default);

    Task<Result<SourceDto>> GetAsync(int id, CancellationToken cancellationToken = default);

    Task<Result<SourceDto>> CreateAsync(
        CreateSourceRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<SourceDto>> UpdateAsync(
        UpdateSourceRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a source and its rules and index rows. Never touches the files on disk — a
    /// misclick in the admin UI must not be able to delete a user's data.
    /// </summary>
    Task<Result> DeleteAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks a host path before it is saved, so the admin finds out the directory is missing or
    /// unreadable at the point of entry rather than the first time someone browses it.
    /// </summary>
    Task<Result<SourcePathProbeDto>> ProbePathAsync(
        string path,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<SourceIndexStatusDto>>> GetIndexStatusAsync(
        CancellationToken cancellationToken = default);
}
