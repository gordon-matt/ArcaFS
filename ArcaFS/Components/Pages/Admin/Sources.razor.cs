using ArcaFS.Components.Shared;

namespace ArcaFS.Components.Pages.Admin;

public partial class Sources
{
    private readonly HashSet<int> scanning = [];
    private string? bannerError;
    private ConfirmDialog? confirmDialog;
    private SourceForm form = new();
    private string? modalError;
    private SourcePathProbeDto? probe;
    private bool saving;
    private bool showEditor;
    private IReadOnlyList<SourceDto>? sources;

    protected override Task OnInitializedAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        var result = await SourceService.ListAsync();
        if (result.IsSuccess)
        {
            sources = result.Value;
        }
        else
        {
            bannerError = result.GetDisplayMessage();
            sources = [];
        }
    }

    private void OpenCreate()
    {
        form = new SourceForm();
        probe = null;
        modalError = null;
        showEditor = true;
    }

    private async Task OpenEdit(SourceDto source)
    {
        form = SourceForm.From(source);
        modalError = null;
        showEditor = true;
        await ProbePathAsync();
    }

    private void OpenAccess(SourceDto source) =>
        Navigation.NavigateTo($"/admin/access?sourceId={source.Id}");

    private void CloseEditor()
    {
        showEditor = false;
        modalError = null;
        probe = null;
    }

    private async Task OnPathCommittedAsync()
    {
        if (string.IsNullOrWhiteSpace(form.Name) && !string.IsNullOrWhiteSpace(form.Path))
        {
            form.Name = new DirectoryInfo(form.Path).Name is { Length: > 0 } name ? name : "Files";
        }

        await ProbePathAsync();
    }

    private async Task ProbePathAsync()
    {
        if (string.IsNullOrWhiteSpace(form.Path))
        {
            probe = null;
            return;
        }

        var result = await SourceService.ProbePathAsync(form.Path);
        probe = result.IsSuccess ? result.Value : null;
    }

    private async Task SaveAsync()
    {
        saving = true;
        modalError = null;

        try
        {
            Result<SourceDto> result = form.Id == 0
                ? await SourceService.CreateAsync(form.ToCreate())
                : await SourceService.UpdateAsync(form.ToUpdate());

            if (result.IsSuccess)
            {
                CloseEditor();
                SidebarState.NotifyChanged();
                Toasts.Success(form.Id == 0 ? "Source added." : "Source updated.");
                await LoadAsync();
            }
            else
            {
                modalError = result.GetDisplayMessage();
            }
        }
        finally
        {
            saving = false;
        }
    }

    private async Task DeleteAsync(SourceDto source)
    {
        if (confirmDialog is null)
        {
            return;
        }

        bool confirmed = await confirmDialog.ShowAsync(
            "Remove source",
            $"Remove “{source.Name}” from ArcaFS?",
            "Remove",
            isDestructive: true,
            detail: "The folder on disk is not deleted.");

        if (!confirmed)
        {
            return;
        }

        var result = await SourceService.DeleteAsync(source.Id);
        if (result.IsSuccess)
        {
            SidebarState.NotifyChanged();
            Toasts.Success("Source removed. Files on disk were left untouched.");
            await LoadAsync();
        }
        else
        {
            bannerError = result.GetDisplayMessage();
        }
    }

    private async Task ScanAsync(SourceDto source)
    {
        scanning.Add(source.Id);
        var result = await IndexService.RequestScanAsync(source.Id);
        scanning.Remove(source.Id);

        if (result.IsSuccess)
        {
            Toasts.Info($"Queued a scan of “{source.Name}”.");
            await LoadAsync();
        }
        else
        {
            bannerError = result.GetDisplayMessage();
        }
    }

    private sealed class SourceForm
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string? Description { get; set; }
        public bool IsReadOnly { get; set; }
        public bool DenyByDefault { get; set; }
        public FilePermission DefaultPermissions { get; set; } = FilePermission.ReadOnly;
        public bool AdministratorsBypassRules { get; set; } = true;
        public string? DefaultUserScope { get; set; }
        public bool CreateUserDirectory { get; set; }
        public bool IsVisibleInSidebar { get; set; } = true;
        public bool IgnoreHiddenFiles { get; set; } = true;
        public bool FollowSymlinks { get; set; }
        public bool IsIndexed { get; set; } = true;
        public int IndexIntervalMinutes { get; set; } = 60;
        public string? ExcludePatterns { get; set; }
        public bool IsDisabled { get; set; }
        public int SortOrder { get; set; }

        public static SourceForm From(SourceDto source) => new()
        {
            Id = source.Id,
            Name = source.Name,
            Path = source.Path,
            Description = source.Description,
            IsReadOnly = source.IsReadOnly,
            DenyByDefault = source.DenyByDefault,
            DefaultPermissions = source.DefaultPermissions,
            AdministratorsBypassRules = source.AdministratorsBypassRules,
            DefaultUserScope = source.DefaultUserScope,
            CreateUserDirectory = source.CreateUserDirectory,
            IsVisibleInSidebar = source.IsVisibleInSidebar,
            IgnoreHiddenFiles = source.IgnoreHiddenFiles,
            FollowSymlinks = source.FollowSymlinks,
            IsIndexed = source.IsIndexed,
            IndexIntervalMinutes = source.IndexIntervalMinutes,
            ExcludePatterns = source.ExcludePatterns,
            IsDisabled = source.IsDisabled,
            SortOrder = source.SortOrder,
        };

        public CreateSourceRequest ToCreate() => new()
        {
            Name = Name,
            Path = Path,
            Description = Description,
            IsReadOnly = IsReadOnly,
            DenyByDefault = DenyByDefault,
            DefaultPermissions = DefaultPermissions,
            AdministratorsBypassRules = AdministratorsBypassRules,
            DefaultUserScope = DefaultUserScope,
            CreateUserDirectory = CreateUserDirectory,
            IsVisibleInSidebar = IsVisibleInSidebar,
            IgnoreHiddenFiles = IgnoreHiddenFiles,
            FollowSymlinks = FollowSymlinks,
            IsIndexed = IsIndexed,
            IndexIntervalMinutes = IndexIntervalMinutes,
            ExcludePatterns = ExcludePatterns,
        };

        public UpdateSourceRequest ToUpdate() => new()
        {
            Id = Id,
            Name = Name,
            Path = Path,
            Description = Description,
            IsReadOnly = IsReadOnly,
            DenyByDefault = DenyByDefault,
            DefaultPermissions = DefaultPermissions,
            AdministratorsBypassRules = AdministratorsBypassRules,
            DefaultUserScope = DefaultUserScope,
            CreateUserDirectory = CreateUserDirectory,
            IsVisibleInSidebar = IsVisibleInSidebar,
            IgnoreHiddenFiles = IgnoreHiddenFiles,
            FollowSymlinks = FollowSymlinks,
            IsIndexed = IsIndexed,
            IndexIntervalMinutes = IndexIntervalMinutes,
            ExcludePatterns = ExcludePatterns,
            IsDisabled = IsDisabled,
            SortOrder = SortOrder,
        };
    }
}
