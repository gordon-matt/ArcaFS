namespace ArcaFS.Services.Shares;

/// <summary>
/// Public links. A share is a capability: it hands a visitor a bounded slice of one path without
/// giving them an account, so the interesting work is bounding it correctly.
/// </summary>
public interface IShareService
{
    /// <summary>Shares owned by the caller, or all of them for an administrator.</summary>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>The shares, or unauthorized when there is no signed-in user.</returns>
    Task<Result<IReadOnlyList<ShareDto>>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a public link. Sharing must be enabled, and the caller must hold share permission
    /// on the path. The stored path is absolute within the source.
    /// </summary>
    /// <param name="request">Source, path, mode, and the optional password, expiry, and limits.</param>
    /// <param name="cancellationToken">Cancels the permission check and the insert.</param>
    /// <returns>
    /// The new share, or a failure when sharing is disabled, the path is missing, or the mode
    /// exceeds what the caller can do there.
    /// </returns>
    Task<Result<ShareDto>> CreateAsync(
        CreateShareRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a share the caller owns, or any share for an administrator. The mode is checked
    /// against the owner's current permission, so a revoked grant cannot be raised.
    /// </summary>
    /// <param name="request">Share id and the fields to store. An empty password leaves the current one.</param>
    /// <param name="cancellationToken">Cancels the lookup and the update.</param>
    /// <returns>The updated share, or a failure when it is missing or no longer allowed.</returns>
    Task<Result<ShareDto>> UpdateAsync(
        UpdateShareRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes a share the caller owns, or any share for an administrator.</summary>
    /// <param name="id">Share id.</param>
    /// <param name="cancellationToken">Cancels the lookup. The delete is not cancelled once it has started.</param>
    /// <returns>Success, or a failure when the share is missing or belongs to someone else.</returns>
    Task<Result> DeleteAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// What the public share page shows before a password is entered. Returns NotFound for an
    /// expired, exhausted or disabled share so probing cannot distinguish those states.
    /// </summary>
    /// <param name="token">Public token from the link.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>The public description, or not found when the link cannot be used.</returns>
    Task<Result<PublicShareInfoDto>> GetPublicInfoAsync(
        string token,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Exchanges a token, and a password when one is set, for the principal a visitor browses
    /// with. The returned principal carries a ceiling and a scope, which is what confines the
    /// visitor to the shared subtree.
    /// </summary>
    /// <param name="token">Public token from the link.</param>
    /// <param name="password">Password when the share has one. Otherwise ignored.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>The visitor principal, or a failure when the token or password is not accepted.</returns>
    Task<Result<ShareSession>> AuthorizeAsync(
        string token,
        string? password,
        CancellationToken cancellationToken = default);

    /// <summary>Bumps the download counter, enforcing <c>MaxDownloads</c>.</summary>
    /// <param name="token">Public token from the link.</param>
    /// <param name="cancellationToken">Cancels the lookup and the counter update.</param>
    /// <returns>Success, or a failure when the share is missing or the download limit is already reached.</returns>
    Task<Result> RecordDownloadAsync(string token, CancellationToken cancellationToken = default);
}
