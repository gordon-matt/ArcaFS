namespace ArcaFS.Services.Shares;

/// <summary>
/// Public links. A share is a capability: it hands a visitor a bounded slice of one path without
/// giving them an account, so the interesting work is bounding it correctly.
/// </summary>
public interface IShareService
{
    /// <summary>Shares owned by the caller, or all of them for an administrator.</summary>
    Task<Result<IReadOnlyList<ShareDto>>> ListAsync(CancellationToken cancellationToken = default);

    Task<Result<ShareDto>> CreateAsync(
        CreateShareRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<ShareDto>> UpdateAsync(
        UpdateShareRequest request,
        CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// What the public share page shows before a password is entered. Returns NotFound for an
    /// expired, exhausted or disabled share so probing cannot distinguish those states.
    /// </summary>
    Task<Result<PublicShareInfoDto>> GetPublicInfoAsync(
        string token,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Exchanges a token, and a password when one is set, for the principal a visitor browses
    /// with. The returned principal carries a ceiling and a scope, which is what confines the
    /// visitor to the shared subtree.
    /// </summary>
    Task<Result<ShareSession>> AuthorizeAsync(
        string token,
        string? password,
        CancellationToken cancellationToken = default);

    /// <summary>Bumps the download counter, enforcing <c>MaxDownloads</c>.</summary>
    Task<Result> RecordDownloadAsync(string token, CancellationToken cancellationToken = default);
}
