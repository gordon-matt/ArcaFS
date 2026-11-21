using ArcaFS.Components.Files;
using ArcaFS.Services.FileSystem;

namespace ArcaFS.Components.Pages;

public partial class Files
{
    private NameConflictDialog? nameConflicts;

    /// <summary>One name that is already in the destination.</summary>
    private readonly record struct NameCollision(string Key, string Name, bool AllowReplace);

    /// <summary>Sent to <c>uploader.js</c>. A skipped file is left out of the list.</summary>
    public sealed record UploadInstruction(
        string RelativePath,
        bool Overwrite,
        string? TargetRelativePath,
        string? TargetFileName);

    private async Task<Dictionary<string, NameConflictChoice>> DecideConflictsAsync(IReadOnlyList<NameCollision> collisions)
    {
        var decisions = new Dictionary<string, NameConflictChoice>(StringComparer.OrdinalIgnoreCase);
        if (collisions.Count == 0 || nameConflicts is null)
        {
            foreach (NameCollision collision in collisions)
            {
                decisions[collision.Key] = NameConflictChoice.Skip;
            }

            return decisions;
        }

        NameConflictChoice? rest = null;
        for (int i = 0; i < collisions.Count; i++)
        {
            NameCollision collision = collisions[i];
            if (rest is { } chosen)
            {
                decisions[collision.Key] = chosen == NameConflictChoice.Replace && !collision.AllowReplace
                    ? NameConflictChoice.Skip
                    : chosen;
                continue;
            }

            NameConflictDialog.NameConflictAnswer answer = await nameConflicts.ShowAsync(
                collision.Name,
                collisions.Count - i - 1,
                collision.AllowReplace);
            if (answer.Choice == NameConflictChoice.Cancel)
            {
                for (int j = i; j < collisions.Count; j++)
                {
                    decisions[collisions[j].Key] = NameConflictChoice.Skip;
                }

                break;
            }

            decisions[collision.Key] = answer.Choice;
            if (answer.ApplyToRest)
            {
                rest = answer.Choice;
            }
        }

        return decisions;
    }

    private async Task TransferResolvedAsync(TransferRequest request, string verb)
    {
        var clear = new List<string>();
        var collisions = new List<NameCollision>();
        Dictionary<string, bool> names = await DestinationNamesAsync(request.DestinationSourceId, request.DestinationPath);
        bool allowReplace = Can(FilePermission.Modify);

        foreach (string path in request.Paths)
        {
            if (IsAlreadyThere(request, path))
            {
                clear.Add(path);
                continue;
            }

            string name = VirtualPath.GetName(path);
            if (names.ContainsKey(name))
            {
                collisions.Add(new NameCollision(path, name, allowReplace));
                continue;
            }

            clear.Add(path);
        }

        Dictionary<string, NameConflictChoice> decisions = await DecideConflictsAsync(collisions);
        var combined = new List<FileOperationResultDto>();
        combined.AddRange(await TransferGroupAsync(request, clear, overwrite: false, renameOnConflict: false));
        combined.AddRange(await TransferGroupAsync(
            request,
            PathsFor(collisions, decisions, NameConflictChoice.Replace),
            overwrite: true,
            renameOnConflict: false));
        combined.AddRange(await TransferGroupAsync(
            request,
            PathsFor(collisions, decisions, NameConflictChoice.Rename),
            overwrite: false,
            renameOnConflict: true));

        List<FileOperationResultDto> unexpected = combined.Where(row => row.NameConflict).ToList();
        if (unexpected.Count > 0)
        {
            combined.RemoveAll(row => row.NameConflict);
            Dictionary<string, NameConflictChoice> extra = await DecideConflictsAsync(
                unexpected.Select(row => new NameCollision(row.Path, VirtualPath.GetName(row.Path), allowReplace)).ToList());
            combined.AddRange(await TransferGroupAsync(
                request,
                extra.Where(pair => pair.Value == NameConflictChoice.Replace).Select(pair => pair.Key).ToList(),
                overwrite: true,
                renameOnConflict: false));
            combined.AddRange(await TransferGroupAsync(
                request,
                extra.Where(pair => pair.Value == NameConflictChoice.Rename).Select(pair => pair.Key).ToList(),
                overwrite: false,
                renameOnConflict: true));
        }

        if (combined.Count == 0)
        {
            return;
        }

        BulkOperationResultDto bulk = BulkOperationResultDto.From(combined);
        ShowBulk(bulk, verb);
        RememberTransfer(request, bulk);

        if (!request.IsCopy && clipboard is { IsCut: true })
        {
            clipboard = null;
        }

        await RefreshAfterChangeAsync();
        if (showTree && folderTree is not null
            && !string.Equals(request.DestinationPath, CurrentPath, StringComparison.OrdinalIgnoreCase))
        {
            await folderTree.RefreshAsync(request.DestinationPath);
        }
    }

    private async Task<List<FileOperationResultDto>> TransferGroupAsync(
        TransferRequest request,
        IReadOnlyList<string> paths,
        bool overwrite,
        bool renameOnConflict)
    {
        if (paths.Count == 0)
        {
            return [];
        }

        Result<BulkOperationResultDto> result = await FileSystemService.TransferAsync(request with
        {
            Paths = paths,
            Overwrite = overwrite,
            RenameOnConflict = renameOnConflict,
        });

        if (!result.IsSuccess)
        {
            return paths.Select(path => new FileOperationResultDto(path, false, Describe(result, "That item could not be transferred."))).ToList();
        }

        return [.. result.Value.Results];
    }

    private static List<string> PathsFor(
        IReadOnlyList<NameCollision> collisions,
        IReadOnlyDictionary<string, NameConflictChoice> decisions,
        NameConflictChoice choice) =>
        collisions
            .Where(collision => decisions.GetValueOrDefault(collision.Key, NameConflictChoice.Skip) == choice)
            .Select(collision => collision.Key)
            .ToList();

    private async Task<Dictionary<string, bool>> DestinationNamesAsync(int sourceId, string path)
    {
        if (listing is not null
            && SourceId == sourceId
            && string.Equals(listing.Path, path, StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(filter)
            && kindFilter == KindFilter.All
            && listing.TotalCount == listing.Items.Count)
        {
            return NamesOf(listing.Items);
        }

        Result<DirectoryListingDto> listed = await FileSystemService.ListAsync(new ListingRequest
        {
            SourceId = sourceId,
            Path = path,
            ShowHidden = true,
            PageSize = 2000,
        });

        return listed.IsSuccess ? NamesOf(listed.Value.Items) : [];
    }

    /// <summary>A copy or move into the folder the item already occupies. The service duplicates a copy and ignores a move.</summary>
    private static bool IsAlreadyThere(TransferRequest request, string path) =>
        request.SourceId == request.DestinationSourceId
        && string.Equals(VirtualPath.GetParent(path) ?? Constants.RootPath, request.DestinationPath, StringComparison.OrdinalIgnoreCase);

    private async Task<List<UploadInstruction>?> PlanUploadsAsync(DroppedFile[] files)
    {
        Dictionary<string, bool> existing = await DestinationNamesAsync(SourceId, CurrentPath);
        var tops = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (DroppedFile file in files)
        {
            string top = TopSegment(file.RelativePath);
            if (seen.Add(top) && existing.ContainsKey(top))
            {
                tops.Add(top);
            }
        }

        if (tops.Count == 0)
        {
            return null;
        }

        bool allowReplace = Can(FilePermission.Modify);
        List<NameCollision> collisions = tops.Select(top =>
        {
            bool existingIsDirectory = existing[top];
            bool incomingIsDirectory = files.Any(file =>
                TopSegment(file.RelativePath).Equals(top, StringComparison.OrdinalIgnoreCase)
                && file.RelativePath.Contains('/'));
            return new NameCollision(top, top, allowReplace && existingIsDirectory == incomingIsDirectory);
        }).ToList();

        Dictionary<string, NameConflictChoice> decisions = await DecideConflictsAsync(collisions);
        var taken = existing.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var renamed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (string top in tops)
        {
            if (decisions.GetValueOrDefault(top) != NameConflictChoice.Rename)
            {
                continue;
            }

            bool incomingIsDirectory = files.Any(file =>
                TopSegment(file.RelativePath).Equals(top, StringComparison.OrdinalIgnoreCase)
                && file.RelativePath.Contains('/'));
            renamed[top] = FreeNames.Reserve(top, incomingIsDirectory, taken);
        }

        var plans = new List<UploadInstruction>(files.Length);
        foreach (DroppedFile file in files)
        {
            string key = string.IsNullOrWhiteSpace(file.RelativePath) ? file.Name : file.RelativePath;
            string top = TopSegment(key);
            if (!existing.ContainsKey(top))
            {
                plans.Add(new UploadInstruction(key, false, null, null));
                continue;
            }

            switch (decisions.GetValueOrDefault(top, NameConflictChoice.Skip))
            {
                case NameConflictChoice.Replace:
                    plans.Add(new UploadInstruction(key, true, null, null));
                    break;
                case NameConflictChoice.Rename when renamed.TryGetValue(top, out string? neu):
                    string rewritten = RewriteTop(key, neu);
                    plans.Add(new UploadInstruction(
                        key,
                        false,
                        rewritten,
                        rewritten.Contains('/') ? null : rewritten));
                    break;
            }
        }

        return plans;
    }

    private static Dictionary<string, bool> NamesOf(IEnumerable<FileSystemItemDto> items)
    {
        var names = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (FileSystemItemDto item in items)
        {
            names.TryAdd(item.Name, item.IsDirectory);
        }

        return names;
    }

    private static string TopSegment(string? relativePath)
    {
        string path = (relativePath ?? string.Empty).Replace('\\', '/');
        int slash = path.IndexOf('/');
        return slash < 0 ? path : path[..slash];
    }

    private static string RewriteTop(string relativePath, string newTop)
    {
        string path = relativePath.Replace('\\', '/');
        int slash = path.IndexOf('/');
        return slash < 0 ? newTop : newTop + path[slash..];
    }
}
