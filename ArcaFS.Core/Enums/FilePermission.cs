namespace ArcaFS.Enums;

/// <summary>
/// The set of things a principal may do at a given path. Stored as a bitmask on
/// <c>AccessRule.Permissions</c> and returned by the permission resolver as the *effective*
/// grant for one (source, path, principal) triple.
/// <para>
/// Create, modify and delete are per path, not global per-user flags. A source is not
/// limited to being read-only for everyone at once: "shared folder writable by me,
/// read-only for everyone else" is two rules on one source.
/// </para>
/// </summary>
[Flags]
public enum FilePermission
{
    /// <summary>No permissions.</summary>
    None = 0,

    /// <summary>See the folder and list its children. Without this the path is invisible.</summary>
    Browse = 1 << 0,

    /// <summary>Open file contents in the browser (preview, editor, media player).</summary>
    Read = 1 << 1,

    /// <summary>Fetch raw bytes — direct download, or a generated archive of a folder.</summary>
    Download = 1 << 2,

    /// <summary>Upload new files and create folders.</summary>
    Create = 1 << 3,

    /// <summary>Overwrite an existing file (editor save, upload-replace).</summary>
    Modify = 1 << 4,

    /// <summary>Rename in place.</summary>
    Rename = 1 << 5,

    /// <summary>Move out of this path. The destination separately needs <see cref="Create"/>.</summary>
    Move = 1 << 6,

    /// <summary>Remove the file or folder. A folder delete is recursive.</summary>
    Delete = 1 << 7,

    /// <summary>Create a public share link for this path.</summary>
    Share = 1 << 8,

    /// <summary>Produce an archive from a selection.</summary>
    Archive = 1 << 9,

    /// <summary>Extract an archive in place. Also needs <see cref="Create"/> for the output.</summary>
    Extract = 1 << 10,

    /// <summary>Edit the access rules that apply at or below this path.</summary>
    ManagePermissions = 1 << 11,

    /// <summary>Look but don't touch. The default for a source that doesn't say otherwise.</summary>
    ReadOnly = Browse | Read | Download,

    /// <summary>Read-only plus everything additive. Notably excludes delete/rename/move.</summary>
    Contributor = ReadOnly | Create | Modify | Archive | Extract,

    /// <summary>Everything except permission management.</summary>
    Editor = Contributor | Rename | Move | Delete | Share,

    /// <summary>Editor plus permission management.</summary>
    Full = Editor | ManagePermissions,
}

/// <summary>Flag tests and display names for <see cref="FilePermission"/>.</summary>
public static class FilePermissionExtensions
{
    /// <summary>Permissions that never mutate the filesystem. Used to clamp read-only sources.</summary>
    public const FilePermission NonMutating = FilePermission.Browse
        | FilePermission.Read
        | FilePermission.Download
        | FilePermission.Archive
        | FilePermission.Share;

    /// <summary>Whether every flag in <paramref name="required"/> is set. <see cref="FilePermission.None"/> is always present.</summary>
    /// <param name="value">Mask to test.</param>
    /// <param name="required">Flags that must all be set.</param>
    /// <returns><see langword="true"/> when the mask includes <paramref name="required"/>.</returns>
    public static bool Has(this FilePermission value, FilePermission required) =>
        required == FilePermission.None || (value & required) == required;

    /// <summary>Whether the mask and <paramref name="any"/> share at least one flag.</summary>
    /// <param name="value">Mask to test.</param>
    /// <param name="any">Flags of which any one is enough.</param>
    /// <returns><see langword="true"/> when the intersection is not empty.</returns>
    public static bool HasAny(this FilePermission value, FilePermission any) => (value & any) != 0;

    /// <summary>
    /// Individual flags set on <paramref name="value"/>, in declaration order. Composite presets
    /// (<see cref="FilePermission.Full"/> etc.) are excluded so the UI can render one chip per flag.
    /// </summary>
    /// <param name="value">Mask to split. Composite presets are not returned as themselves.</param>
    /// <returns>The individual flags that are set.</returns>
    public static IEnumerable<FilePermission> ToFlags(this FilePermission value) =>
        AllFlags.Where(flag => (value & flag) == flag);

    /// <summary>The individual flags, in declaration order, excluding the composite presets.</summary>
    public static readonly IReadOnlyList<FilePermission> AllFlags =
    [
        FilePermission.Browse,
        FilePermission.Read,
        FilePermission.Download,
        FilePermission.Create,
        FilePermission.Modify,
        FilePermission.Rename,
        FilePermission.Move,
        FilePermission.Delete,
        FilePermission.Share,
        FilePermission.Archive,
        FilePermission.Extract,
        FilePermission.ManagePermissions,
    ];

    /// <summary>A label for one flag or a known preset. An unknown combination uses the enum name.</summary>
    /// <param name="flag">Flag, preset, or other combination.</param>
    /// <returns>The label.</returns>
    public static string ToDisplayName(this FilePermission flag) => flag switch
    {
        FilePermission.None => "None",
        FilePermission.Browse => "Browse",
        FilePermission.Read => "Read",
        FilePermission.Download => "Download",
        FilePermission.Create => "Create",
        FilePermission.Modify => "Modify",
        FilePermission.Rename => "Rename",
        FilePermission.Move => "Move",
        FilePermission.Delete => "Delete",
        FilePermission.Share => "Share",
        FilePermission.Archive => "Archive",
        FilePermission.Extract => "Extract",
        FilePermission.ManagePermissions => "Manage permissions",
        FilePermission.ReadOnly => "Read only",
        FilePermission.Contributor => "Contributor",
        FilePermission.Editor => "Editor",
        FilePermission.Full => "Full control",
        _ => flag.ToString(),
    };

    /// <summary>
    /// Renders a mask as a comma-separated list of its individual permissions, for messages
    /// like "Missing: Create, Modify". <see cref="ToDisplayName"/> only names a mask that
    /// happens to be a single flag or a known preset.
    /// </summary>
    /// <param name="value">Mask to render. <see cref="FilePermission.None"/> is the word None.</param>
    /// <returns>The comma-separated labels.</returns>
    public static string ToDisplayList(this FilePermission value) => value == FilePermission.None ? "None" : string.Join(", ", value.ToDisplayNames());

    /// <summary>
    /// The display name of each set flag, for rendering a mask as individual chips rather than
    /// one string.
    /// </summary>
    /// <param name="value">Mask to render.</param>
    /// <returns>One label per set individual flag.</returns>
    public static IEnumerable<string> ToDisplayNames(this FilePermission value) =>
        value.ToFlags().Select(f => f.ToDisplayName());
}