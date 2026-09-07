using System.Runtime.CompilerServices;

namespace ArcaFS.Desktop;

/// <summary>
/// Locates the shared <c>wwwroot</c> folder used by both the web project and this
/// Electron-hosted desktop project.
/// </summary>
internal static class SharedAssetLocator
{
    public static string ResolveWebRoot()
    {
        string? shared = TryGetRepoSharedWebRoot();
        if (shared is not null && Directory.Exists(shared))
        {
            return shared;
        }

        string published = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        Directory.CreateDirectory(published);
        return published;
    }

    private static string? TryGetRepoSharedWebRoot([CallerFilePath] string thisFilePath = "")
    {
        if (string.IsNullOrEmpty(thisFilePath))
        {
            return null;
        }

        string? desktopDir = Path.GetDirectoryName(thisFilePath);
        string? repoDir = Path.GetDirectoryName(desktopDir);
        return repoDir is null ? null : Path.Combine(repoDir, "ArcaFS", "wwwroot");
    }
}
