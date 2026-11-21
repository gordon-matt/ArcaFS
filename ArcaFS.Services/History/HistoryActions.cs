namespace ArcaFS.Services.History;

/// <summary>A file or folder that was created. Undo removes it only while it is still empty.</summary>
public sealed class CreateAction(int sourceId, string parentPath, string name, bool isDirectory, string createdPath) : HistoryAction
{
    public override async Task<HistoryOutcome> UndoAsync(IFileSystemService service, CancellationToken cancellationToken)
    {
        if (isDirectory)
        {
            Result<DirectoryListingDto> children = await service.ListAsync(
                new ListingRequest
                {
                    SourceId = sourceId,
                    Path = createdPath,
                    ShowHidden = true,
                    PageSize = 1,
                },
                cancellationToken: cancellationToken);
            if (!children.IsSuccess)
            {
                return HistoryOutcome.From(children);
            }

            // Files added after the folder was created are not on the undo stack. Deleting the
            // folder would take them with it, so the folder stays until it is empty again.
            if (children.Value.TotalCount > 0)
            {
                return HistoryOutcome.Refuse();
            }
        }
        else
        {
            Result<FileDetailDto> detail = await service.GetDetailAsync(sourceId, createdPath, cancellationToken);
            if (!detail.IsSuccess)
            {
                return HistoryOutcome.From(detail);
            }

            if (detail.Value.SizeBytes > 0)
            {
                return HistoryOutcome.Refuse();
            }
        }

        return HistoryOutcome.FromBulk(await service.DeleteAsync(
            new DeleteRequest { SourceId = sourceId, Paths = [createdPath] },
            cancellationToken));
    }

    public override async Task<HistoryOutcome> RedoAsync(IFileSystemService service, CancellationToken cancellationToken)
    {
        if (isDirectory)
        {
            Result<FileSystemItemDto> created = await service.CreateDirectoryAsync(
                new CreateDirectoryRequest { SourceId = sourceId, Path = parentPath, Name = name },
                cancellationToken);
            if (!created.IsSuccess)
            {
                return HistoryOutcome.From(created);
            }

            createdPath = created.Value.Path;
            return HistoryOutcome.Ok();
        }

        Result<TextSaveDto> file = await service.SaveTextAsync(
            new SaveTextFileRequest { SourceId = sourceId, Path = createdPath, Content = string.Empty },
            cancellationToken);
        if (!file.IsSuccess)
        {
            return HistoryOutcome.From(file);
        }

        createdPath = file.Value.Path;
        return HistoryOutcome.Ok();
    }
}

/// <summary>A rename. The paths are updated after each step because the name may be adjusted.</summary>
public sealed class RenameAction(int sourceId, string fromPath, string fromName, string toPath, string toName) : HistoryAction
{
    public override async Task<HistoryOutcome> UndoAsync(IFileSystemService service, CancellationToken cancellationToken)
    {
        Result<FileSystemItemDto> renamed = await service.RenameAsync(
            new RenameRequest { SourceId = sourceId, Path = toPath, NewName = fromName },
            cancellationToken);
        if (!renamed.IsSuccess)
        {
            return HistoryOutcome.From(renamed);
        }

        fromPath = renamed.Value.Path;
        fromName = renamed.Value.Name;
        return HistoryOutcome.Ok();
    }

    public override async Task<HistoryOutcome> RedoAsync(IFileSystemService service, CancellationToken cancellationToken)
    {
        Result<FileSystemItemDto> renamed = await service.RenameAsync(
            new RenameRequest { SourceId = sourceId, Path = fromPath, NewName = toName },
            cancellationToken);
        if (!renamed.IsSuccess)
        {
            return HistoryOutcome.From(renamed);
        }

        toPath = renamed.Value.Path;
        toName = renamed.Value.Name;
        return HistoryOutcome.Ok();
    }
}

/// <summary>One item in a copy or move. <see cref="ResultPath"/> is where it landed.</summary>
public sealed class TransferredItem(string sourcePath, string resultPath)
{
    public string SourcePath { get; } = sourcePath;

    public string ResultPath { get; set; } = resultPath;
}

/// <summary>A copy, duplicate, or move. Undo of a copy deletes the new items. Undo of a move puts them back.</summary>
public sealed class TransferAction(
    int sourceId,
    int destinationSourceId,
    string destinationPath,
    bool isCopy,
    List<TransferredItem> items) : HistoryAction
{
    public override Task<HistoryOutcome> UndoAsync(IFileSystemService service, CancellationToken cancellationToken) =>
        isCopy ? DeleteCopiesAsync(service, cancellationToken) : MoveBackAsync(service, cancellationToken);

    public override async Task<HistoryOutcome> RedoAsync(IFileSystemService service, CancellationToken cancellationToken)
    {
        Result<BulkOperationResultDto> moved = await service.TransferAsync(
            new TransferRequest
            {
                SourceId = sourceId,
                Paths = items.Select(item => item.SourcePath).ToList(),
                DestinationSourceId = destinationSourceId,
                DestinationPath = destinationPath,
                IsCopy = isCopy,
                RenameOnConflict = isCopy,
            },
            cancellationToken);
        HistoryOutcome outcome = HistoryOutcome.FromBulk(moved);
        if (!moved.IsSuccess)
        {
            return outcome;
        }

        if (!outcome.Succeeded)
        {
            await RollBackPartialAsync(service, moved.Value, cancellationToken);
            return outcome;
        }

        foreach (FileOperationResultDto row in moved.Value.Results)
        {
            if (!row.Succeeded || row.ResultPath is null)
            {
                continue;
            }

            TransferredItem? item = items.FirstOrDefault(candidate =>
                string.Equals(candidate.SourcePath, row.Path, StringComparison.OrdinalIgnoreCase));
            if (item is not null)
            {
                item.ResultPath = row.ResultPath;
            }
        }

        return HistoryOutcome.Ok();
    }

    private async Task<HistoryOutcome> DeleteCopiesAsync(IFileSystemService service, CancellationToken cancellationToken)
    {
        // A result path equal to its source is the original. Deleting it would remove the file
        // the copy was taken from.
        List<string> paths = items
            .Where(item => !string.Equals(item.SourcePath, item.ResultPath, StringComparison.OrdinalIgnoreCase))
            .Select(item => item.ResultPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (paths.Count == 0)
        {
            return HistoryOutcome.Ok();
        }

        return HistoryOutcome.FromBulk(await service.DeleteAsync(
            new DeleteRequest { SourceId = destinationSourceId, Paths = paths },
            cancellationToken));
    }

    private async Task<HistoryOutcome> MoveBackAsync(IFileSystemService service, CancellationToken cancellationToken)
    {
        List<IGrouping<string, TransferredItem>> groups = items
            .GroupBy(item => VirtualPath.GetParent(item.SourcePath) ?? Constants.RootPath, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var done = new List<IGrouping<string, TransferredItem>>();

        foreach (IGrouping<string, TransferredItem> group in groups)
        {
            Result<BulkOperationResultDto> moved = await service.TransferAsync(
                new TransferRequest
                {
                    SourceId = destinationSourceId,
                    Paths = group.Select(item => item.ResultPath).ToList(),
                    DestinationSourceId = sourceId,
                    DestinationPath = group.Key,
                    IsCopy = false,
                },
                cancellationToken);
            HistoryOutcome outcome = HistoryOutcome.FromBulk(moved);
            if (!outcome.Succeeded)
            {
                foreach (IGrouping<string, TransferredItem> finished in done)
                {
                    await service.TransferAsync(
                        new TransferRequest
                        {
                            SourceId = sourceId,
                            Paths = finished.Select(item => item.SourcePath).ToList(),
                            DestinationSourceId = destinationSourceId,
                            DestinationPath = destinationPath,
                            IsCopy = false,
                        },
                        cancellationToken);
                }

                return outcome;
            }

            done.Add(group);
        }

        return HistoryOutcome.Ok();
    }

    private async Task RollBackPartialAsync(
        IFileSystemService service,
        BulkOperationResultDto result,
        CancellationToken cancellationToken)
    {
        List<FileOperationResultDto> written = result.Results
            .Where(row => row.Succeeded && !string.IsNullOrEmpty(row.ResultPath))
            .ToList();
        if (written.Count == 0)
        {
            return;
        }

        if (isCopy)
        {
            await service.DeleteAsync(
                new DeleteRequest
                {
                    SourceId = destinationSourceId,
                    Paths = written.Select(row => row.ResultPath!).ToList(),
                },
                cancellationToken);
            return;
        }

        IEnumerable<IGrouping<string, FileOperationResultDto>> groups = written.GroupBy(
            row =>
            {
                TransferredItem? item = items.FirstOrDefault(candidate =>
                    string.Equals(candidate.SourcePath, row.Path, StringComparison.OrdinalIgnoreCase));
                return VirtualPath.GetParent(item?.SourcePath ?? row.Path) ?? Constants.RootPath;
            },
            StringComparer.OrdinalIgnoreCase);

        foreach (IGrouping<string, FileOperationResultDto> group in groups)
        {
            await service.TransferAsync(
                new TransferRequest
                {
                    SourceId = destinationSourceId,
                    Paths = group.Select(row => row.ResultPath!).ToList(),
                    DestinationSourceId = sourceId,
                    DestinationPath = group.Key,
                    IsCopy = false,
                },
                cancellationToken);
        }
    }
}

/// <summary>
/// One save from the text editor. The bytes are checked before they are replaced, so a save that
/// happened somewhere else is left alone. The timestamp guard is skipped because an earlier undo
/// has already written the file and moved that timestamp.
/// </summary>
public sealed class TextEditAction(int sourceId, string path, string before, string after) : HistoryAction
{
    public override Task<HistoryOutcome> UndoAsync(IFileSystemService service, CancellationToken cancellationToken) =>
        ReplaceAsync(service, expected: after, replacement: before, cancellationToken);

    public override Task<HistoryOutcome> RedoAsync(IFileSystemService service, CancellationToken cancellationToken) =>
        ReplaceAsync(service, expected: before, replacement: after, cancellationToken);

    private async Task<HistoryOutcome> ReplaceAsync(
        IFileSystemService service,
        string expected,
        string replacement,
        CancellationToken cancellationToken)
    {
        Result<TextFileContentDto> current = await service.ReadTextAsync(sourceId, path, cancellationToken);
        if (!current.IsSuccess)
        {
            return HistoryOutcome.From(current);
        }

        if (!string.Equals(current.Value.Content, expected, StringComparison.Ordinal))
        {
            return HistoryOutcome.Refuse();
        }

        Result<TextSaveDto> saved = await service.SaveTextAsync(
            new SaveTextFileRequest { SourceId = sourceId, Path = path, Content = replacement },
            cancellationToken);
        return HistoryOutcome.From(saved);
    }
}
