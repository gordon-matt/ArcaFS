namespace ArcaFS.Services.Sources;

/// <summary>
/// Administration of sources. Reading which sources a *user* can see is
/// <see cref="IPermissionService.GetAccessibleSourcesAsync"/>; everything here is administrator-only.
/// </summary>
public interface ISourceService
{
    /// <summary>Every source, with how many access rules it has.</summary>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>The sources, ordered by sort order and then name, or forbidden when the caller is not an administrator.</returns>
    Task<Result<IReadOnlyList<SourceDto>>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>One source, including its access-rule count.</summary>
    /// <param name="id">Source id.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>The source, or a failure when it is missing or the caller is not an administrator.</returns>
    Task<Result<SourceDto>> GetAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Creates a source. This does not create the host directory.</summary>
    /// <param name="request">Name, host path, and the browsing and indexing options.</param>
    /// <param name="cancellationToken">Cancels the validation and the insert.</param>
    /// <returns>The new source, or a failure when the caller is not an administrator or the request is invalid.</returns>
    Task<Result<SourceDto>> CreateAsync(
        CreateSourceRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces a source. Changing the host path clears that source's index, because the old
    /// rows describe a tree that is no longer attached. Cached permissions are dropped either way.
    /// </summary>
    /// <param name="request">Source id and the fields to store.</param>
    /// <param name="cancellationToken">Cancels the lookup, the update, and the index clear.</param>
    /// <returns>The stored source, or a failure when it is missing, invalid, or the caller is not an administrator.</returns>
    Task<Result<SourceDto>> UpdateAsync(
        UpdateSourceRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a source and its rules and index rows. Never touches the files on disk — a
    /// misclick in the admin UI must not be able to delete a user's data.
    /// </summary>
    /// <param name="id">Source id.</param>
    /// <param name="cancellationToken">Cancels the lookup and the deletes.</param>
    /// <returns>Success, or a failure when the source is missing or the caller is not an administrator.</returns>
    Task<Result> DeleteAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks a host path before it is saved, so the admin finds out the directory is missing or
    /// unreadable at the point of entry rather than the first time someone browses it.
    /// A path outside the configured data root is reported as not usable. This still returns
    /// success for a path that fails the check; the probe payload says why.
    /// </summary>
    /// <param name="path">Host path to inspect. Blank is invalid.</param>
    /// <param name="cancellationToken">Not observed.</param>
    /// <returns>
    /// The probe, or forbidden when the caller is not an administrator. An unreadable folder is
    /// still a successful probe with <c>CanRead</c> false.
    /// </returns>
    Task<Result<SourcePathProbeDto>> ProbePathAsync(
        string path,
        CancellationToken cancellationToken = default);

    /// <summary>Whether each source is scanning, and the last completed index totals.</summary>
    /// <param name="cancellationToken">Cancels the source lookup. The in-memory scan flags are read afterward.</param>
    /// <returns>One status per source, or forbidden when the caller is not an administrator.</returns>
    Task<Result<IReadOnlyList<SourceIndexStatusDto>>> GetIndexStatusAsync(
        CancellationToken cancellationToken = default);
}
