using ArcaFS.Components.Files;
using Microsoft.JSInterop;

namespace ArcaFS.Components.Pages;

public partial class Files
{
    private KindFilter kindFilter = KindFilter.All;
    private bool showTree;
    private FolderTree? folderTree;

    private static readonly (KindFilter Filter, string Icon, string Label)[] KindFilters =
    [
        (KindFilter.All, "bi-collection", "All"),
        (KindFilter.Images, "bi-image", "Images"),
        (KindFilter.Documents, "bi-file-earmark-text", "Documents"),
        (KindFilter.Media, "bi-play-circle", "Media"),
        (KindFilter.Archives, "bi-file-earmark-zip", "Archives"),
    ];

    private async Task SetKindFilterAsync(KindFilter value)
    {
        kindFilter = value;
        pageSize = 500;
        await LoadAsync();
    }

    private void InvertSelection()
    {
        if (listing is null)
        {
            return;
        }

        var next = new HashSet<string>(StringComparer.Ordinal);
        foreach (FileSystemItemDto item in listing.Items)
        {
            if (!selection.Contains(item.Path))
            {
                next.Add(item.Path);
            }
        }

        selection.Clear();
        foreach (string path in next)
        {
            selection.Add(path);
        }

        rangeAnchorPath = selection.FirstOrDefault();
    }

    private async Task ToggleTreeAsync()
    {
        showTree = !showTree;
        await JS.InvokeVoidAsync("arcafs.setStoredShowTree", showTree ? "true" : "false");
    }

    private async Task DuplicateAsync()
    {
        List<string> paths = selection.Count > 0
            ? selection.ToList()
            : contextMenuItem is null ? [] : [contextMenuItem.Path];

        if (paths.Count == 0)
        {
            return;
        }

        // A folder chosen in the tree may not be in the open folder. Copy it beside itself,
        // which is also where a listing item already lives.
        string parent = VirtualPath.GetParent(paths[0]) ?? Constants.RootPath;
        bool sameParent = paths.All(path => string.Equals(
            VirtualPath.GetParent(path) ?? Constants.RootPath,
            parent,
            StringComparison.OrdinalIgnoreCase));

        await TransferResolvedAsync(new TransferRequest
        {
            SourceId = SourceId,
            Paths = paths,
            DestinationSourceId = SourceId,
            DestinationPath = sameParent ? parent : CurrentPath,
            IsCopy = true,
        }, "Copied");
    }

    [JSInvokable]
    public void OnItemDragStart(string path)
    {
        if (string.IsNullOrEmpty(path) || selection.Contains(path))
        {
            return;
        }

        selection.Clear();
        selection.Add(path);
        rangeAnchorPath = path;
        StateHasChanged();
    }

    [JSInvokable]
    public async Task OnItemsDropped(string sourcePath, string targetPath, bool copy)
    {
        if (string.IsNullOrEmpty(sourcePath) || string.IsNullOrEmpty(targetPath))
        {
            return;
        }

        List<string> paths = selection.Contains(sourcePath) ? selection.ToList() : [sourcePath];
        if (paths.Any(path =>
            VirtualPath.IsSameOrUnder(targetPath, path)
            || string.Equals(VirtualPath.GetParent(path) ?? Constants.RootPath, targetPath, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        if (!copy && !Can(FilePermission.Move))
        {
            Toasts.Error("You don't have permission to move these items.");
            return;
        }

        await TransferResolvedAsync(new TransferRequest
        {
            SourceId = SourceId,
            Paths = paths,
            DestinationSourceId = SourceId,
            DestinationPath = targetPath,
            IsCopy = copy,
        }, copy ? "Copied" : "Moved");
    }
}
