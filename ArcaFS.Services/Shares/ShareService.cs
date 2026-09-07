using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;

namespace ArcaFS.Services.Shares;

public sealed class ShareService(
    ILogger<ShareService> logger,
    IUserContextService userContext,
    IUserInfoService userInfoService,
    IPermissionService permissionService,
    IServerSettingsService settingsService,
    IActivityService activityService,
    IRepository<Share> repository,
    IRepository<Source> sourceRepository) : IShareService
{
    /// <summary>
    /// 32 bytes of randomness, base64url encoded. A share token is a bearer credential, so it
    /// has to be long enough that guessing is hopeless.
    /// </summary>
    private const int TokenBytes = 32;

    /// <summary>
    /// Hashes share passwords with the same PBKDF2 configuration Identity uses for accounts,
    /// rather than a bare hash, so a leaked database doesn't hand over the passwords.
    /// </summary>
    private static readonly PasswordHasher<object> PasswordHasher = new();

    private static readonly object PasswordHashUser = new();

    public async Task<Result<IReadOnlyList<ShareDto>>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        string? userId = userContext.GetCurrentUserId();
        if (userId is null)
        {
            return Result<IReadOnlyList<ShareDto>>.Unauthorized();
        }

        bool isAdministrator = userContext.IsAdministrator();

        var shares = await repository.FindAsync(new SearchOptions<Share>
        {
            Query = isAdministrator ? null : s => s.OwnerUserId == userId,
            Include = q => q.Include(s => s.Source),
            OrderBy = q => q.OrderByDescending(s => s.CreatedAt),
            CancellationToken = cancellationToken,
        });

        var list = shares.ToList();

        var owners = await userInfoService.GetUserInfoAsync(
            list.Select(s => s.OwnerUserId).Distinct(StringComparer.Ordinal),
            cancellationToken);

        IReadOnlyList<ShareDto> dtos = list
            .Select(s => ToDto(s, owners.GetValueOrDefault(s.OwnerUserId)?.Label))
            .ToList();

        return Result<IReadOnlyList<ShareDto>>.Success(dtos);
    }

    public async Task<Result<ShareDto>> CreateAsync(
        CreateShareRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!await settingsService.GetAsync(Constants.ServerSettingKeys.SharesEnabled, true, cancellationToken))
        {
            return Result<ShareDto>.Forbidden("Sharing is disabled on this server.");
        }

        string? userId = userContext.GetCurrentUserId();
        if (userId is null)
        {
            return Result<ShareDto>.Unauthorized();
        }

        // Share requires more than Read: handing out a public link is a bigger act than opening
        // the file yourself, so it is a permission of its own.
        var resolved = await permissionService.RequireAsync(
            request.SourceId,
            request.Path,
            FilePermission.Share,
            cancellationToken);

        if (!resolved.IsSuccess)
        {
            return resolved.To<ShareDto>();
        }

        var target = resolved.Value;

        var validation = ValidateMode(request.Mode, target);
        if (!validation.IsSuccess)
        {
            return validation.To<ShareDto>();
        }

        bool isDirectory = Directory.Exists(target.PhysicalPath);
        if (!isDirectory && !File.Exists(target.PhysicalPath))
        {
            return Result<ShareDto>.NotFound($"'{request.Path}' was not found.");
        }

        var share = new Share
        {
            Token = GenerateToken(),
            SourceId = request.SourceId,

            // Stored absolute within the source. The creator may be scoped, and their scope
            // prefix must survive in the share or the link would resolve somewhere else.
            Path = target.AbsoluteVirtualPath,
            OwnerUserId = userId,
            Title = request.Title?.Trim(),
            Description = request.Description?.Trim(),
            Mode = request.Mode,
            PasswordHash = HashPassword(request.Password),
            ExpiresAt = request.ExpiresIn is TimeSpan lifetime
                ? DateTime.UtcNow.Add(lifetime)
                : null,
            MaxDownloads = request.MaxDownloads,
            DisableThumbnails = request.DisableThumbnails,
            DisableFileViewer = request.DisableFileViewer,
            Theme = request.Theme,
        };

        var created = await repository.InsertAsync(share);
        created.Source = target.Source;

        logger.LogInformation(
            "Share {ShareId} created for source {SourceId} path {Path} in {Mode} mode by {User}",
            created.Id,
            request.SourceId,
            target.AbsoluteVirtualPath,
            request.Mode,
            userContext.GetCurrentUserName());

        await activityService.RecordAsync(
            ActivityKind.ShareCreated,
            request.SourceId,
            target.ScopedVirtualPath,
            shareToken: created.Token,
            detail: $"{request.Mode} share",
            cancellationToken: cancellationToken);

        return Result<ShareDto>.Success(ToDto(created, userContext.GetCurrentUserName()));
    }

    public async Task<Result<ShareDto>> UpdateAsync(
        UpdateShareRequest request,
        CancellationToken cancellationToken = default)
    {
        var found = await FindOwnedAsync(request.Id, cancellationToken);
        if (!found.IsSuccess)
        {
            return found.To<ShareDto>();
        }

        var share = found.Value;

        // Re-checked on every update: the owner's permissions may have been revoked since the
        // share was created, and raising its mode must not be a way around that.
        var resolved = await permissionService.RequireAsync(
            share.SourceId,
            share.Path,
            FilePermission.Share,
            cancellationToken);

        if (!resolved.IsSuccess)
        {
            return resolved.To<ShareDto>();
        }

        var validation = ValidateMode(request.Mode, resolved.Value);
        if (!validation.IsSuccess)
        {
            return validation.To<ShareDto>();
        }

        share.Title = request.Title?.Trim();
        share.Description = request.Description?.Trim();
        share.Mode = request.Mode;
        share.MaxDownloads = request.MaxDownloads;
        share.DisableThumbnails = request.DisableThumbnails;
        share.DisableFileViewer = request.DisableFileViewer;
        share.Theme = request.Theme;
        share.IsDisabled = request.IsDisabled;

        if (request.RemovePassword)
        {
            share.PasswordHash = null;
        }
        else if (!string.IsNullOrEmpty(request.Password))
        {
            share.PasswordHash = HashPassword(request.Password);
        }

        if (request.ExpiresIn is TimeSpan lifetime)
        {
            share.ExpiresAt = DateTime.UtcNow.Add(lifetime);
        }

        await repository.UpdateAsync(share);

        return Result<ShareDto>.Success(ToDto(share, userContext.GetCurrentUserName()));
    }

    public async Task<Result> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var found = await FindOwnedAsync(id, cancellationToken);
        if (!found.IsSuccess)
        {
            return found.ToVoid();
        }

        await repository.DeleteAsync(found.Value);

        logger.LogInformation(
            "Share {ShareId} deleted by {User}",
            id,
            userContext.GetCurrentUserName());

        return Result.Success();
    }

    public async Task<Result<PublicShareInfoDto>> GetPublicInfoAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        var share = await FindUsableAsync(token, cancellationToken);
        if (share is null)
        {
            // Uniformly NotFound. Distinguishing "expired" from "never existed" would let
            // someone enumerate tokens.
            return Result<PublicShareInfoDto>.NotFound();
        }

        bool isSingleFile = false;
        string? fileName = null;
        long? fileSize = null;
        var kind = FileKind.Folder;

        if (VirtualPath.TryResolve(share.Source.Path, share.Path, out string? physicalPath))
        {
            if (File.Exists(physicalPath))
            {
                var info = new FileInfo(physicalPath);
                isSingleFile = true;
                fileName = info.Name;
                fileSize = info.Length;
                kind = FileKinds.Classify(info.Name, isDirectory: false);
            }
            else if (!Directory.Exists(physicalPath))
            {
                // The shared path is gone. Same answer as a bad token.
                return Result<PublicShareInfoDto>.NotFound();
            }
        }

        return Result<PublicShareInfoDto>.Success(new PublicShareInfoDto(
            share.Token,
            share.Title,
            share.Description,
            share.PasswordHash is not null,
            isSingleFile,
            fileName,
            fileSize,
            kind,
            share.Mode,
            share.DisableThumbnails,
            share.DisableFileViewer,
            share.Theme,
            share.ExpiresAt));
    }

    public async Task<Result<ShareSession>> AuthorizeAsync(
        string token,
        string? password,
        CancellationToken cancellationToken = default)
    {
        var share = await FindUsableAsync(token, cancellationToken);
        if (share is null)
        {
            return Result<ShareSession>.NotFound();
        }

        if (share.PasswordHash is not null && !VerifyPassword(share.PasswordHash, password))
        {
            await activityService.RecordAsync(
                ActivityKind.ShareAccessed,
                share.SourceId,
                share.Path,
                succeeded: false,
                shareToken: share.Token,
                detail: "Incorrect password",
                overrideUserId: Constants.AnonymousUserId,
                cancellationToken: cancellationToken);

            return Result<ShareSession>.Unauthorized("That password is not correct.");
        }

        // The visitor's ceiling is the share's mode intersected with what the owner can actually
        // do at that path *now*. If the owner has since lost write access, a read-write share
        // silently degrades to read-only rather than outliving their permissions.
        var ownerPermissions = await ResolveOwnerPermissionsAsync(share, cancellationToken);
        var ceiling = ShareSession.CeilingFor(share.Mode) & ownerPermissions;

        if (!ceiling.HasAny(FilePermission.Browse | FilePermission.Read))
        {
            logger.LogWarning(
                "Share {ShareId} is unusable because its owner no longer has access to {Path}",
                share.Id,
                share.Path);

            return Result<ShareSession>.NotFound();
        }

        bool isSingleFile = VirtualPath.TryResolve(share.Source.Path, share.Path, out string? physical)
            && File.Exists(physical);

        share.LastAccessedAt = DateTime.UtcNow;
        await repository.UpdateAsync(share);

        await activityService.RecordAsync(
            ActivityKind.ShareAccessed,
            share.SourceId,
            share.Path,
            shareToken: share.Token,
            overrideUserId: Constants.AnonymousUserId,
            cancellationToken: cancellationToken);

        return Result<ShareSession>.Success(new ShareSession
        {
            Token = share.Token,
            SourceId = share.SourceId,
            RootPath = share.Path,
            IsSingleFile = isSingleFile,
            Mode = share.Mode,
            DisableThumbnails = share.DisableThumbnails,
            DisableFileViewer = share.DisableFileViewer,
            Theme = share.Theme,
            Principal = AccessPrincipal.Anonymous with { Ceiling = ceiling },
        });
    }

    public async Task<Result> RecordDownloadAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        var share = await repository.FindOneAsync(new SearchOptions<Share>
        {
            Query = s => s.Token == token,
            CancellationToken = cancellationToken,
        });

        if (share is null)
        {
            return Result.NotFound();
        }

        if (share.MaxDownloads is int max && share.DownloadCount >= max)
        {
            return Result.Forbidden("This link has reached its download limit.");
        }

        share.DownloadCount++;
        share.LastAccessedAt = DateTime.UtcNow;
        await repository.UpdateAsync(share);

        return Result.Success();
    }

    /// <summary>
    /// A share can never grant more than its creator holds. Resolved as the owner rather than
    /// the caller, because the caller here is an anonymous visitor.
    /// </summary>
    private async Task<FilePermission> ResolveOwnerPermissionsAsync(
        Share share,
        CancellationToken cancellationToken)
    {
        var owner = await userInfoService.GetUserAsync(share.OwnerUserId, cancellationToken);

        var ownerPrincipal = new AccessPrincipal
        {
            UserId = share.OwnerUserId,
            UserName = owner?.UserName,
            IsAuthenticated = true,
            IsAdministrator = owner?.IsAdministrator ?? false,
            RoleNames = owner is null
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(owner.Roles, StringComparer.OrdinalIgnoreCase),
        };

        var resolved = await permissionService.RequireAsync(
            share.SourceId,
            share.Path,
            FilePermission.None,
            ownerPrincipal,
            cancellationToken);

        return resolved.IsSuccess ? resolved.Value.Permissions : FilePermission.None;
    }

    /// <summary>
    /// Rejects a share mode the creator could not perform themselves — offering an upload share
    /// on a folder you cannot write to would produce a link that fails on first use.
    /// </summary>
    private static Result ValidateMode(ShareMode mode, ResolvedPath target)
    {
        var needed = ShareSession.CeilingFor(mode);

        if (mode != ShareMode.ReadOnly && target.Source.IsReadOnly)
        {
            return Result.Invalid(new ValidationError(
                nameof(mode), $"'{target.Source.Name}' is read-only, so only view links can be created."));
        }

        var missing = needed & ~target.Permissions;

        if (missing != FilePermission.None)
        {
            return Result.Invalid(new ValidationError(
                nameof(mode),
                "You cannot create a link that grants more than you can do yourself. " +
                $"Missing: {missing.ToDisplayList()}."));
        }

        return Result.Success();
    }

    private async Task<Result<Share>> FindOwnedAsync(int id, CancellationToken cancellationToken)
    {
        string? userId = userContext.GetCurrentUserId();
        if (userId is null)
        {
            return Result<Share>.Unauthorized();
        }

        var share = await repository.FindOneAsync(new SearchOptions<Share>
        {
            Query = s => s.Id == id,
            Include = q => q.Include(s => s.Source),
            CancellationToken = cancellationToken,
        });

        if (share is null)
        {
            return Result<Share>.NotFound($"Share {id} was not found.");
        }

        bool isOwner = string.Equals(share.OwnerUserId, userId, StringComparison.Ordinal);

        return isOwner || userContext.IsAdministrator()
            ? Result<Share>.Success(share)
            : Result<Share>.Forbidden();
    }

    /// <summary>
    /// Loads a share only if it is currently usable. Every public entry point goes through this
    /// so the expiry, disabled and download-limit checks can't be forgotten in one of them.
    /// </summary>
    private async Task<Share?> FindUsableAsync(string token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var share = await repository.FindOneAsync(new SearchOptions<Share>
        {
            Query = s => s.Token == token,
            Include = q => q.Include(s => s.Source),
            CancellationToken = cancellationToken,
        });

        if (share is null || share.IsDisabled || share.Source.IsDisabled)
        {
            return null;
        }

        if (share.ExpiresAt is DateTime expiry && expiry <= DateTime.UtcNow)
        {
            return null;
        }

        if (share.MaxDownloads is int max && share.DownloadCount >= max)
        {
            return null;
        }

        return share;
    }

    private static ShareDto ToDto(Share share, string? ownerDisplayName) => new(
        share.Id,
        share.Token,
        share.SourceId,
        share.Source?.Name ?? string.Empty,
        share.Path,
        share.OwnerUserId,
        ownerDisplayName,
        share.Title,
        share.Description,
        share.Mode,
        share.PasswordHash is not null,
        share.ExpiresAt,
        share.MaxDownloads,
        share.DownloadCount,
        share.DisableThumbnails,
        share.DisableFileViewer,
        share.Theme,
        share.CreatedAt,
        share.LastAccessedAt,
        share.IsDisabled);

    private static string GenerateToken()
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(TokenBytes);

        // Base64url so the token is safe in a path segment without escaping.
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    private static string? HashPassword(string? password) =>
        string.IsNullOrEmpty(password)
            ? null
            : PasswordHasher.HashPassword(PasswordHashUser, password);

    private static bool VerifyPassword(string hash, string? password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return false;
        }

        var outcome = PasswordHasher.VerifyHashedPassword(PasswordHashUser, hash, password);

        return outcome is PasswordVerificationResult.Success
            or PasswordVerificationResult.SuccessRehashNeeded;
    }
}
