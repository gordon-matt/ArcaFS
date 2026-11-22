namespace ArcaFS.Enums;

/// <summary>
/// Who an <c>AccessRule</c> targets. The numeric values double as the *specificity* ranking
/// used by the resolver: a higher value wins over a lower one at the same path depth, so a
/// user-specific rule always beats the group rule that would otherwise apply to them.
/// </summary>
public enum AccessSubjectType
{
    /// <summary>Anyone, signed in or not.</summary>
    Everyone = 0,

    /// <summary>Share-link visitors with no account.</summary>
    Anonymous = 1,

    /// <summary>Any signed-in user.</summary>
    Authenticated = 2,

    /// <summary>An ASP.NET Identity / Keycloak role. <c>SubjectId</c> holds the role name.</summary>
    Role = 3,

    /// <summary>An ArcaFS access group. <c>SubjectId</c> holds the group id.</summary>
    Group = 4,

    /// <summary>
    /// Whoever the path belongs to. Pairs with a templated path such as <c>/${username}</c>:
    /// one rule then grants every user full control of their own home directory and nothing else.
    /// </summary>
    Owner = 5,

    /// <summary>A single user. <c>SubjectId</c> holds the user id.</summary>
    User = 6,
}

public enum AccessEffect
{
    /// <summary>Adds the rule's permissions.</summary>
    Allow = 0,

    /// <summary>Applied after <see cref="Allow"/> at the same specificity, so deny wins on a tie.</summary>
    Deny = 1,
}

/// <summary>How far down the tree a rule reaches from its own path.</summary>
public enum AccessPropagation
{
    /// <summary>The rule's path and everything under it. The usual choice.</summary>
    ThisPathAndChildren = 0,

    /// <summary>Only the rule's own path. Lets you grant Browse on a folder without its contents.</summary>
    ThisPathOnly = 1,

    /// <summary>Everything under the path but not the path itself.</summary>
    ChildrenOnly = 2,
}