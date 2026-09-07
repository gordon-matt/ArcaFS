namespace ArcaFS.Models;

/// <summary>Steps of the first-run wizard, in order.</summary>
public static class SetupSteps
{
    public const string Welcome = "welcome";
    public const string Admin = "admin";
    public const string Source = "source";
    public const string Done = "done";
}

public record SetupAdminRequest
{
    public string UserName { get; init; } = string.Empty;

    public string Email { get; init; } = string.Empty;

    public string Password { get; init; } = string.Empty;
}

public record SetupSourceRequest
{
    public string Name { get; init; } = string.Empty;

    public string Path { get; init; } = string.Empty;

    /// <summary>
    /// Applies the "per-user home directories" template: a <c>${username}</c> scope plus an
    /// Owner rule granting full control of one's own folder and nothing else.
    /// </summary>
    public bool UsePerUserDirectories { get; init; }

    public bool IsReadOnly { get; init; }
}

public record SetupStateDto(
    bool IsComplete,
    bool HasAdministrator,
    bool HasSource,
    string AuthProviderName,
    string DatabaseProviderName);

/// <summary>A directory entry offered by the server-side folder picker used when adding a source.</summary>
public record HostDirectoryDto(string Name, string FullPath, bool HasChildren);

public record HostDirectoryListingDto(
    string? CurrentPath,
    string? ParentPath,
    IReadOnlyList<HostDirectoryDto> Directories,
    IReadOnlyList<HostDirectoryDto> Roots);
