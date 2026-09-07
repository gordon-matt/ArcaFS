using Microsoft.AspNetCore.Identity;

namespace ArcaFS.Infrastructure;

/// <summary>
/// Brings the database up to date at startup and seeds the minimum an instance needs to be
/// usable.
/// </summary>
public static class DbInitializer
{
    public static async Task InitializeAsync(
        IServiceProvider services,
        AuthProvider authProvider,
        IConfiguration configuration,
        ILogger logger)
    {
        var context = services.GetRequiredService<ApplicationDbContextBase>();

        if (context.Database.IsRelational())
        {
            logger.LogInformation("Applying database migrations");
            await context.Database.MigrateAsync();
        }
        else
        {
            // The in-memory provider has no migrations; it is only used when no connection
            // string is configured.
            await context.Database.EnsureCreatedAsync();
        }

        switch (authProvider)
        {
            case AuthProvider.Identity:
                await SeedRolesAsync(services, logger);
                await SeedAdministratorAsync(services, configuration, logger);
                break;

            case AuthProvider.Keycloak:
                // Users and roles live in the IdP, so there is no local administrator to create
                // and nothing for the wizard's admin step to do.
                await SeedRolesAsync(services, logger);
                break;

            case AuthProvider.None:
                // The synthetic user is always an administrator, so the wizard would only ask
                // questions with one possible answer. Skip straight past it.
                await MarkSetupCompleteAsync(services, logger);
                break;
        }

        await EnsureDesktopSourceAsync(services, authProvider, configuration, logger);
    }

    private static async Task SeedRolesAsync(IServiceProvider services, ILogger logger)
    {
        var roleManager = services.GetService<RoleManager<ApplicationRole>>();

        if (roleManager is null)
        {
            return;
        }

        var roles = new (string Name, string Description)[]
        {
            (Constants.Roles.Administrator, "Full access to every source and to server administration"),
            (Constants.Roles.User, "Standard access, governed by access rules"),
        };

        foreach (var (name, description) in roles)
        {
            if (await roleManager.RoleExistsAsync(name))
            {
                continue;
            }

            await roleManager.CreateAsync(new ApplicationRole { Name = name, Description = description });
            logger.LogInformation("Created role '{Role}'", name);
        }
    }

    /// <summary>
    /// Creates the seed administrator when configuration supplies one and no administrator
    /// exists yet. Without a configured password this does nothing, leaving the first-run
    /// wizard to create the account interactively — which is the better default, since a
    /// well-known seeded password on a reachable server is a liability.
    /// </summary>
    private static async Task SeedAdministratorAsync(
        IServiceProvider services,
        IConfiguration configuration,
        ILogger logger)
    {
        var userManager = services.GetService<UserManager<ApplicationUser>>();

        if (userManager is null)
        {
            return;
        }

        var existing = await userManager.GetUsersInRoleAsync(Constants.Roles.Administrator);
        if (existing.Count > 0)
        {
            return;
        }

        string? password = configuration["Seed:Admin:Password"];

        if (string.IsNullOrWhiteSpace(password))
        {
            logger.LogInformation(
                "No administrator exists and Seed:Admin:Password is not set. " +
                "The first-run wizard will create one.");

            return;
        }

        string email = configuration["Seed:Admin:Email"] ?? "admin@arcafs.local";
        string userName = configuration["Seed:Admin:UserName"] ?? email;

        var user = new ApplicationUser
        {
            UserName = userName,
            Email = email,
            EmailConfirmed = true,
            DisplayName = "Administrator",
            CanCreateShares = true,
            CanUseApi = true,
        };

        var created = await userManager.CreateAsync(user, password);

        if (!created.Succeeded)
        {
            logger.LogError(
                "Could not create the seed administrator: {Errors}",
                string.Join("; ", created.Errors.Select(e => e.Description)));

            return;
        }

        await userManager.AddToRoleAsync(user, Constants.Roles.Administrator);

        logger.LogWarning(
            "Seed administrator '{UserName}' was created from configuration. Change its password.",
            userName);
    }

    private static async Task MarkSetupCompleteAsync(IServiceProvider services, ILogger logger)
    {
        var settingsService = services.GetRequiredService<IServerSettingsService>();

        if (await settingsService.IsSetupCompleteAsync())
        {
            return;
        }

        await settingsService.MarkSetupCompleteAsync();
        logger.LogInformation("Authentication is disabled, so the first-run wizard was skipped");
    }

    /// <summary>
    /// Creates a source from <c>Desktop:DefaultSourcePath</c> when none exists. This is what
    /// lets the desktop build open on something useful instead of an empty sidebar and a wizard
    /// the user was promised they would not have to complete.
    /// </summary>
    private static async Task EnsureDesktopSourceAsync(
        IServiceProvider services,
        AuthProvider authProvider,
        IConfiguration configuration,
        ILogger logger)
    {
        if (authProvider != AuthProvider.None)
        {
            return;
        }

        string? path = configuration["Desktop:DefaultSourcePath"];

        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return;
        }

        var repository = services.GetRequiredService<IRepository<Source>>();

        if (await repository.CountAsync(s => true) > 0)
        {
            return;
        }

        string fullPath = Path.GetFullPath(path);

        await repository.InsertAsync(new Source
        {
            Name = new DirectoryInfo(fullPath).Name is { Length: > 0 } name ? name : "Files",
            Path = fullPath,
            Description = "Created automatically for the desktop build",
            DefaultPermissions = FilePermission.Full & ~FilePermission.ManagePermissions,
        });

        logger.LogInformation("Created the default desktop source at {Path}", fullPath);
    }
}
