namespace ArcaFS.Infrastructure;

/// <summary>
/// Lets pages tell the sidebar its data is stale.
/// </summary>
/// <remarks>
/// The sidebar shows sources and favourites, but both are changed from pages that sit outside
/// its render tree — the admin source editor, the star button in the file toolbar. Scoped to the
/// circuit, so one user's changes never nudge another's sidebar.
/// </remarks>
public sealed class SidebarState
{
    public event Action? Changed;

    public void NotifyChanged() => Changed?.Invoke();
}
