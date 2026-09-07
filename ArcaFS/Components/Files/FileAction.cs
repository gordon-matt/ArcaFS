namespace ArcaFS.Components.Files;

/// <summary>
/// Actions the toolbar, context menu and keyboard shortcuts all raise, so the page handles each
/// one in a single place regardless of how it was triggered.
/// </summary>
public enum FileAction
{
    Open,
    Preview,
    Edit,
    Download,
    Rename,
    Copy,
    Cut,
    Paste,
    Delete,
    Compress,
    Extract,
    Share,
    ToggleFavorite,
    Info,
    ManageAccess,
    NewFolder,
    NewFile,
    Upload,
    UploadFolder,
    SelectAll,
    ClearSelection,
    Refresh,
}
