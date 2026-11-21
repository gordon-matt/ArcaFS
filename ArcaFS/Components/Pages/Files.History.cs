using ArcaFS.Services.History;

namespace ArcaFS.Components.Pages;

public partial class Files
{
    private async Task UndoAsync()
    {
        if (History.TryUndo() is not { } action)
        {
            return;
        }

        HistoryOutcome outcome = await action.UndoAsync(FileSystemService);
        if (!outcome.Succeeded)
        {
            History.RevertUndo();
            Toasts.Error(outcome.Blocked
                ? "That change can't be undone because the item was changed afterwards."
                : outcome.Error ?? "That change could not be undone.");
            return;
        }

        Toasts.Success("Undone.");
        selection.Clear();
        await RefreshAfterChangeAsync();
    }

    private async Task RedoAsync()
    {
        if (History.TryRedo() is not { } action)
        {
            return;
        }

        HistoryOutcome outcome = await action.RedoAsync(FileSystemService);
        if (!outcome.Succeeded)
        {
            History.RevertRedo();
            Toasts.Error(outcome.Blocked
                ? "That change can't be redone because the item was changed afterwards."
                : outcome.Error ?? "That change could not be redone.");
            return;
        }

        Toasts.Success("Redone.");
        selection.Clear();
        await RefreshAfterChangeAsync();
    }

    private void RememberCreate(FileSystemItemDto created, bool isDirectory)
    {
        string parent = VirtualPath.GetParent(created.Path) ?? Constants.RootPath;
        History.Push(new CreateAction(SourceId, parent, created.Name, isDirectory, created.Path));
    }

    private void RememberRename(string fromPath, string fromName, FileSystemItemDto after) =>
        History.Push(new RenameAction(SourceId, fromPath, fromName, after.Path, after.Name));

    private void RememberTransfer(TransferRequest request, BulkOperationResultDto result)
    {
        var transferred = new List<TransferredItem>();
        foreach (FileOperationResultDto row in result.Results)
        {
            if (!row.Succeeded || string.IsNullOrEmpty(row.ResultPath))
            {
                continue;
            }

            if (string.Equals(row.Path, row.ResultPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            transferred.Add(new TransferredItem(row.Path, row.ResultPath));
        }

        if (transferred.Count == 0)
        {
            return;
        }

        History.Push(new TransferAction(
            request.SourceId,
            request.DestinationSourceId,
            request.DestinationPath,
            request.IsCopy,
            transferred));
    }

    private async Task RefreshAfterChangeAsync()
    {
        await LoadAsync();
        if (showTree && folderTree is not null)
        {
            await folderTree.RefreshAsync(CurrentPath);
        }

        // Drag-and-drop arrives through JS interop, which does not re-render the page the way a
        // button click does. Without this the listing keeps showing a file that has already moved.
        StateHasChanged();
    }
}
