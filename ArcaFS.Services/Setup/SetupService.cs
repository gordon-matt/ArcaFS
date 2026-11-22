using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ArcaFS.Services.Setup;

/// <inheritdoc cref="ISetupService" />
public sealed class SetupService(
    ILogger<SetupService> logger,
    IServiceProvider serviceProvider,
    IAuthProviderService authProviderService,
    IServerSettingsService settingsService,
    IRepository<Source> sourceRepository,
    IRepository<AccessRule> ruleRepository,
    IUserContextService userContext,
    IOptions<HostBrowseOptions> browseOptions,
    string databaseProviderName) : ISetupService
{
    /// <inheritdoc />
    public async Task<SetupStateDto> GetStateAsync(CancellationToken cancellationToken = default)
    {
        bool isComplete = await settingsService.IsSetupCompleteAsync(cancellationToken);
        int sourceCount = await sourceRepository.CountAsync(s => true);

        return new SetupStateDto(
            isComplete,
            await HasAdministratorAsync(cancellationToken),
            sourceCount > 0,
            authProviderService.Provider.ToString(),
            databaseProviderName);
    }

    /// <inheritdoc />
    public async Task<Result> CreateAdministratorAsync(
        SetupAdminRequest request,
        CancellationToken cancellationToken = default)
    {
        var gate = await EnsureSetupWindowAsync(cancellationToken);
        if (!gate.IsSuccess)
        {
            return gate;
        }

        if (authProviderService.Provider != AuthProvider.Identity)
        {
            return Result.Invalid(new ValidationError(
                nameof(request.UserName),
                authProviderService.Provider == AuthProvider.Keycloak
                    ? "Users are managed in Keycloak, so no local administrator is created here."
                    : "Authentication is disabled, so there is no administrator to create."));
        }

        // The one case where creating an administrator without being one is legitimate. Guarded
        // by EnsureSetupWindowAsync, which refuses once an administrator already exists.
        if (await HasAdministratorAsync(cancellationToken))
        {
            return Result.Conflict("An administrator already exists.");
        }

        if (string.IsNullOrWhiteSpace(request.UserName))
        {
            return Result.Invalid(new ValidationError(nameof(request.UserName), "Enter a username."));
        }

        if (string.IsNullOrWhiteSpace(request.Password))
        {
            return Result.Invalid(new ValidationError(nameof(request.Password), "Enter a password."));
        }

        using var scope = serviceProvider.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();

        await EnsureRolesAsync(roleManager);

        var user = new ApplicationUser
        {
            UserName = request.UserName.Trim(),
            Email = request.Email?.Trim(),
            EmailConfirmed = true,
            DisplayName = request.UserName.Trim(),
            CanCreateShares = true,
            CanUseApi = true,
        };

        var created = await userManager.CreateAsync(user, request.Password);

        if (!created.Succeeded)
        {
            // Identity's messages are already user-facing ("Passwords must have at least one
            // digit"), so they are surfaced as validation errors rather than replaced.
            return Result.Invalid(created.Errors
                .Select(e => new ValidationError(nameof(request.Password), e.Description))
                .ToArray());
        }

        await userManager.AddToRoleAsync(user, WellKnownRoles.Administrator);

        logger.LogInformation(
            "First administrator '{UserName}' created by the setup wizard",
            user.UserName);

        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<SourceDto>> CreateFirstSourceAsync(
        SetupSourceRequest request,
        CancellationToken cancellationToken = default)
    {
        var gate = await EnsureSetupWindowAsync(cancellationToken);
        if (!gate.IsSuccess)
        {
            return gate.To<SourceDto>();
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Result<SourceDto>.Invalid(new ValidationError(
                nameof(request.Name), "Enter a name for this source."));
        }

        if (string.IsNullOrWhiteSpace(request.Path) || !Directory.Exists(request.Path))
        {
            return Result<SourceDto>.Invalid(new ValidationError(
                nameof(request.Path), "That folder does not exist on the server."));
        }

        string sourcePath = Path.GetFullPath(request.Path.Trim());

        if (OutsideDataRoot(sourcePath) is string outsideMessage)
        {
            return Result<SourceDto>.Invalid(new ValidationError(nameof(request.Path), outsideMessage));
        }

        var source = new Source
        {
            Name = request.Name.Trim(),
            Path = sourcePath,
            IsReadOnly = request.IsReadOnly,
        };

        if (request.UsePerUserDirectories)
        {
            // Deny by default with an Owner rule is what makes each user's folder private; a
            // permissive default would let everyone read everyone else's home.
            source.DefaultUserScope = $"/{Constants.PathTokens.UserName}";
            source.CreateUserDirectory = true;
            source.DenyByDefault = true;
            source.DefaultPermissions = FilePermission.None;
        }
        else
        {
            source.DefaultPermissions = request.IsReadOnly
                ? FilePermission.ReadOnly
                : FilePermission.Editor;
        }

        var created = await sourceRepository.InsertAsync(source);

        if (request.UsePerUserDirectories)
        {
            await ruleRepository.InsertAsync(new AccessRule
            {
                SourceId = created.Id,
                Path = $"/{Constants.PathTokens.UserName}",
                SubjectType = AccessSubjectType.Owner,
                Effect = AccessEffect.Allow,
                Permissions = FilePermission.Full & ~FilePermission.ManagePermissions,
                Propagation = AccessPropagation.ThisPathAndChildren,
                Description = "Each user has full control of their own folder",
            });
        }

        logger.LogInformation(
            "First source '{Name}' created at {Path} by the setup wizard (per-user folders: {PerUser})",
            created.Name,
            created.Path,
            request.UsePerUserDirectories);

        return Result<SourceDto>.Success(new SourceDto(
            created.Id,
            created.Name,
            created.Path,
            created.Description,
            created.IsDisabled,
            created.IsReadOnly,
            created.DenyByDefault,
            created.DefaultPermissions,
            created.AdministratorsBypassRules,
            created.DefaultUserScope,
            created.CreateUserDirectory,
            created.IsVisibleInSidebar,
            created.IgnoreHiddenFiles,
            created.FollowSymlinks,
            created.IsIndexed,
            created.IndexIntervalMinutes,
            created.ExcludePatterns,
            null,
            null,
            null,
            created.SortOrder,
            request.UsePerUserDirectories ? 1 : 0,
            true));
    }

    /// <inheritdoc />
    public async Task<Result> CompleteAsync(CancellationToken cancellationToken = default)
    {
        var gate = await EnsureSetupWindowAsync(cancellationToken);
        if (!gate.IsSuccess)
        {
            return gate;
        }

        await settingsService.MarkSetupCompleteAsync(cancellationToken);

        logger.LogInformation("First-run setup completed");

        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<HostDirectoryListingDto>> BrowseHostDirectoriesAsync(
        string? path,
        CancellationToken cancellationToken = default)
    {
        // Exposes the host's directory structure, so it is limited to the setup window and to
        // administrators once setup is done.
        bool isSetupComplete = await settingsService.IsSetupCompleteAsync(cancellationToken);

        if (isSetupComplete && !userContext.IsAdministrator())
        {
            return Result<HostDirectoryListingDto>.Forbidden();
        }

        bool jailed = HostBrowsePaths.TryGetEnforcedRoot(browseOptions.Value.DataRoot, out string dataRoot);
        IReadOnlyList<HostDirectoryDto> roots = jailed
            ? [DescribeDirectory(dataRoot)]
            : GetRoots();

        // With no jail and no path, offer the machine's drives (or the usual Linux roots).
        if (!jailed && string.IsNullOrWhiteSpace(path))
        {
            return Result<HostDirectoryListingDto>.Success(
                new HostDirectoryListingDto(null, null, [], roots));
        }

        string requested = string.IsNullOrWhiteSpace(path) ? dataRoot : path;
        string full;

        try
        {
            full = Path.GetFullPath(requested);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Result<HostDirectoryListingDto>.Invalid(new ValidationError(
                nameof(path), "That is not a valid path."));
        }

        if (!Directory.Exists(full))
        {
            return Result<HostDirectoryListingDto>.NotFound($"'{full}' does not exist.");
        }

        if (jailed && !HostBrowsePaths.IsAllowed(full, dataRoot))
        {
            return Result<HostDirectoryListingDto>.Invalid(new ValidationError(
                nameof(path),
                $"Choose a folder inside {dataRoot}."));
        }

        List<HostDirectoryDto> directories;

        try
        {
            directories = ListChildDirectories(full, jailed ? dataRoot : null, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result<HostDirectoryListingDto>.Error(
                "The server does not have permission to read that folder.");
        }

        string? parent = jailed
            ? HostBrowsePaths.ParentWithinRoot(full, dataRoot)
            : Path.GetDirectoryName(full);

        return Result<HostDirectoryListingDto>.Success(new HostDirectoryListingDto(
            full,
            parent,
            directories,
            roots));
    }

    /// <summary>
    /// Null when the path is inside the data directory, or when no data directory is enforced.
    /// </summary>
    private string? OutsideDataRoot(string fullPath)
    {
        if (!HostBrowsePaths.TryGetEnforcedRoot(browseOptions.Value.DataRoot, out string dataRoot))
        {
            return null;
        }

        return HostBrowsePaths.IsAllowed(fullPath, dataRoot)
            ? null
            : $"Choose a folder inside {dataRoot}.";
    }

    /// <summary>
    /// Refuses every mutating wizard action once setup is complete. Without this, the
    /// unauthenticated setup endpoints would remain a way to mint an administrator account.
    /// </summary>
    private async Task<Result> EnsureSetupWindowAsync(CancellationToken cancellationToken)
    {
        if (!await settingsService.IsSetupCompleteAsync(cancellationToken))
        {
            return Result.Success();
        }

        // After setup, an administrator may still legitimately re-run parts of the wizard.
        return userContext.IsAdministrator()
            ? Result.Success()
            : Result.Forbidden("Setup has already been completed.");
    }

    private async Task<bool> HasAdministratorAsync(CancellationToken cancellationToken)
    {
        if (authProviderService.Provider != AuthProvider.Identity)
        {
            // Keycloak owns its own admins, and None mode's synthetic user is always one.
            return true;
        }

        try
        {
            using var scope = serviceProvider.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            var administrators = await userManager.GetUsersInRoleAsync(WellKnownRoles.Administrator);

            return administrators.Count > 0;
        }
        catch (Exception ex)
        {
            // Before migrations run the Identity tables don't exist, which means there is
            // certainly no administrator yet.
            logger.LogDebug(ex, "Could not check for an existing administrator");
            return false;
        }
    }

    private static async Task EnsureRolesAsync(RoleManager<ApplicationRole> roleManager)
    {
        foreach (string role in new[] { WellKnownRoles.Administrator, WellKnownRoles.User })
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new ApplicationRole
                {
                    Name = role,
                    Description = role == WellKnownRoles.Administrator
                        ? "Full access to every source and to server administration"
                        : "Standard access, governed by access rules",
                });
            }
        }
    }

    private static HostDirectoryDto DescribeDirectory(string path)
    {
        var info = new DirectoryInfo(path);
        string name = info.Name.Length > 0 ? info.Name : path;

        return new HostDirectoryDto(name, path, HasAccessibleChildren(info));
    }

    private static List<HostDirectoryDto> ListChildDirectories(
        string full,
        string? jailRoot,
        CancellationToken cancellationToken)
    {
        var directories = new List<HostDirectoryDto>();

        foreach (var directory in new DirectoryInfo(full)
            .EnumerateDirectories("*", new EnumerationOptions { IgnoreInaccessible = true }))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (jailRoot is not null && !HostBrowsePaths.IsAllowed(directory.FullName, jailRoot))
            {
                continue;
            }

            directories.Add(new HostDirectoryDto(
                directory.Name,
                directory.FullName,
                HasAccessibleChildren(directory)));
        }

        return directories.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Starting points when no data directory is enforced. On Linux these are the paths that
    /// actually matter on a host; on Windows they are the drives.
    /// </summary>
    private static IReadOnlyList<HostDirectoryDto> GetRoots()
    {
        var roots = new List<HostDirectoryDto>();

        if (OperatingSystem.IsWindows())
        {
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
            {
                roots.Add(new HostDirectoryDto(
                    drive.Name,
                    drive.RootDirectory.FullName,
                    HasAccessibleChildren(drive.RootDirectory)));
            }

            return roots;
        }

        foreach (string candidate in new[] { "/data", "/srv", "/mnt", "/media", "/home", "/" })
        {
            if (Directory.Exists(candidate))
            {
                roots.Add(new HostDirectoryDto(
                    candidate,
                    candidate,
                    HasAccessibleChildren(new DirectoryInfo(candidate))));
            }
        }

        return roots;
    }

    /// <summary>
    /// Whether the picker should show an expand arrow. Enumerates lazily and stops at the first
    /// hit so this stays cheap on a directory with a hundred thousand entries.
    /// </summary>
    private static bool HasAccessibleChildren(DirectoryInfo directory)
    {
        try
        {
            return directory
                .EnumerateDirectories("*", new EnumerationOptions { IgnoreInaccessible = true })
                .Any();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}