using ArcaFS.Components.Shared;
using Microsoft.AspNetCore.Components;

namespace ArcaFS.Components.Pages.Admin;

public partial class Access
{
    private bool applyingTemplate;
    private string? bannerError;
    private ConfirmDialog? confirmDialog;
    private string? explainError;
    private EffectivePermissionsDto? explanation;
    private RuleForm form = new();
    private IReadOnlyList<AccessGroupDto> groups = [];
    private string? modalError;
    private readonly Dictionary<string, bool> replaceExistingByTemplate = [];
    private IReadOnlyList<AccessRuleDto>? rules;
    private bool saving;
    private SourceDto? selectedSource;
    private int selectedSourceId;
    private bool showEditor;
    private string simulatePath = Constants.RootPath;
    private string? simulateUserId;
    private IReadOnlyList<SourceDto>? sources;
    private IReadOnlyList<AccessTemplateDto> templates = [];
    private string? templateGroupId;
    private string? templateOwnerUserId;
    private IReadOnlyList<UserInfo> users = [];

    [SupplyParameterFromQuery(Name = "sourceId")]
    public int? QuerySourceId { get; set; }

    [SupplyParameterFromQuery(Name = "path")]
    public string? QueryPath { get; set; }

    protected override async Task OnInitializedAsync()
    {
        var sourceResult = await SourceService.ListAsync();
        sources = sourceResult.IsSuccess ? sourceResult.Value : [];

        users = await UserInfoService.GetAllUsersAsync();
        simulateUserId = UserContext.GetCurrentUserId();

        var groupResult = await AccessGroupService.ListAsync();
        groups = groupResult.IsSuccess ? groupResult.Value : [];

        if (QuerySourceId is int id && sources.Any(s => s.Id == id))
        {
            selectedSourceId = id;
            simulatePath = string.IsNullOrWhiteSpace(QueryPath) ? Constants.RootPath : QueryPath;
            await LoadRulesAsync();
        }
    }

    private async Task OnSourceChangedAsync(ChangeEventArgs e)
    {
        selectedSourceId = int.TryParse(e.Value?.ToString(), out int id) ? id : 0;
        explanation = null;
        await LoadRulesAsync();
    }

    private async Task LoadRulesAsync()
    {
        selectedSource = sources?.FirstOrDefault(s => s.Id == selectedSourceId);
        rules = null;
        templates = [];

        if (selectedSourceId <= 0)
        {
            return;
        }

        templates = await AccessRuleService.GetTemplatesAsync(selectedSourceId);

        var result = await AccessRuleService.ListAsync(selectedSourceId);
        if (result.IsSuccess)
        {
            rules = result.Value;
        }
        else
        {
            bannerError = result.GetDisplayMessage();
            rules = [];
        }
    }

    private void OpenCreate()
    {
        form = new RuleForm
        {
            SourceId = selectedSourceId,
            Path = string.IsNullOrWhiteSpace(QueryPath) ? Constants.RootPath : QueryPath,
        };
        modalError = null;
        showEditor = true;
    }

    private void OpenEdit(AccessRuleDto rule)
    {
        form = RuleForm.From(rule);
        modalError = null;
        showEditor = true;
    }

    private void CloseEditor()
    {
        showEditor = false;
        modalError = null;
    }

    private async Task SaveAsync()
    {
        saving = true;
        modalError = null;

        try
        {
            Result result = form.Id == 0
                ? (await AccessRuleService.CreateAsync(form.ToCreate())).ToVoid()
                : (await AccessRuleService.UpdateAsync(form.ToUpdate())).ToVoid();

            if (result.IsSuccess)
            {
                CloseEditor();
                Toasts.Success(form.Id == 0 ? "Rule added." : "Rule updated.");
                await LoadRulesAsync();
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

    private async Task DeleteAsync(AccessRuleDto rule)
    {
        if (confirmDialog is null)
        {
            return;
        }

        bool confirmed = await confirmDialog.ShowAsync(
            "Delete rule",
            $"Delete the {rule.Effect} rule for {rule.SubjectType} at {rule.Path}?",
            "Delete",
            isDestructive: true);

        if (!confirmed)
        {
            return;
        }

        var result = await AccessRuleService.DeleteAsync(rule.Id);
        if (result.IsSuccess)
        {
            Toasts.Success("Rule deleted.");
            await LoadRulesAsync();
        }
        else
        {
            bannerError = result.GetDisplayMessage();
        }
    }

    private bool IsReplaceExisting(string templateKey) =>
        replaceExistingByTemplate.GetValueOrDefault(templateKey);

    private void SetReplaceExisting(string templateKey, bool value) =>
        replaceExistingByTemplate[templateKey] = value;

    private async Task ApplyTemplateAsync(AccessTemplateDto template)
    {
        applyingTemplate = true;
        bannerError = null;

        try
        {
            string? subjectId = template.Key switch
            {
                AccessTemplates.SharedReadOnlyOwnerWrites =>
                    string.IsNullOrWhiteSpace(templateOwnerUserId)
                        ? UserContext.GetCurrentUserId()
                        : templateOwnerUserId,
                AccessTemplates.GroupWorkspace => templateGroupId,
                _ => UserContext.GetCurrentUserId(),
            };

            var result = await AccessRuleService.ApplyTemplateAsync(
                selectedSourceId,
                template.Key,
                subjectId,
                IsReplaceExisting(template.Key));

            if (result.IsSuccess)
            {
                Toasts.Success($"Applied “{template.Name}”.");
                await LoadRulesAsync();
            }
            else
            {
                bannerError = result.GetDisplayMessage();
            }
        }
        finally
        {
            applyingTemplate = false;
        }
    }

    private async Task ExplainAsync()
    {
        explainError = null;

        if (selectedSourceId <= 0)
        {
            explainError = "Select a source first.";
            return;
        }

        var result = await PermissionService.ExplainAsync(new EffectivePermissionsRequest
        {
            SourceId = selectedSourceId,
            Path = string.IsNullOrWhiteSpace(simulatePath) ? Constants.RootPath : simulatePath,
            UserId = string.IsNullOrWhiteSpace(simulateUserId) ? null : simulateUserId,
        });

        if (result.IsSuccess)
        {
            explanation = result.Value;
        }
        else
        {
            explainError = result.GetDisplayMessage();
            explanation = null;
        }
    }

    private static string FormatPropagation(AccessPropagation propagation) => propagation switch
    {
        AccessPropagation.ThisPathAndChildren => "This folder and children",
        AccessPropagation.ThisPathOnly => "This folder only",
        AccessPropagation.ChildrenOnly => "Children only",
        _ => propagation.ToString(),
    };

    private sealed class RuleForm
    {
        public int Id { get; set; }
        public int SourceId { get; set; }
        public string Path { get; set; } = Constants.RootPath;
        public AccessSubjectType SubjectType { get; set; } = AccessSubjectType.Authenticated;
        public string? SubjectId { get; set; }
        public AccessEffect Effect { get; set; } = AccessEffect.Allow;
        public FilePermission Permissions { get; set; } = FilePermission.ReadOnly;
        public AccessPropagation Propagation { get; set; } = AccessPropagation.ThisPathAndChildren;
        public int Priority { get; set; }
        public string? Description { get; set; }

        public static RuleForm From(AccessRuleDto rule) => new()
        {
            Id = rule.Id,
            SourceId = rule.SourceId,
            Path = rule.Path,
            SubjectType = rule.SubjectType,
            SubjectId = rule.SubjectId,
            Effect = rule.Effect,
            Permissions = rule.Permissions,
            Propagation = rule.Propagation,
            Priority = rule.Priority,
            Description = rule.Description,
        };

        public CreateAccessRuleRequest ToCreate() => new()
        {
            SourceId = SourceId,
            Path = Path,
            SubjectType = SubjectType,
            SubjectId = SubjectId,
            Effect = Effect,
            Permissions = Permissions,
            Propagation = Propagation,
            Priority = Priority,
            Description = Description,
        };

        public UpdateAccessRuleRequest ToUpdate() => new()
        {
            Id = Id,
            SourceId = SourceId,
            Path = Path,
            SubjectType = SubjectType,
            SubjectId = SubjectId,
            Effect = Effect,
            Permissions = Permissions,
            Propagation = Propagation,
            Priority = Priority,
            Description = Description,
        };
    }
}
