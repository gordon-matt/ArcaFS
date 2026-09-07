using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace ArcaFS.Infrastructure;

/// <summary>
/// Short-lived, single-use tickets that let a Blazor component hand a multi-item zip request to
/// a plain browser navigation.
/// </summary>
/// <remarks>
/// A zip of a large selection can't be a GET with the paths in the query string — a few hundred
/// files blow past every URL length limit. It also can't usefully be a <c>fetch</c> POST, because
/// that buffers the whole archive in browser memory instead of streaming it to disk. Registering
/// the selection and navigating to a ticket URL gets a native, streaming download with no length
/// limit. The ticket is unguessable, expires quickly, is consumed on use and is bound to the
/// owner who created it, so it isn't a way to reach files by other means.
/// </remarks>
public sealed class DownloadTicketService(TimeProvider timeProvider)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, Ticket> tickets = new(StringComparer.Ordinal);

    /// <summary>
    /// Registers a selection and returns its ticket.
    /// </summary>
    /// <param name="owner">
    /// Who may redeem it. A user id for a signed-in caller, or <see cref="ShareOwner"/> for a
    /// public share visitor, so one share's ticket can't be redeemed against another.
    /// </param>
    public string Create(string owner, int sourceId, IReadOnlyList<string> paths, string fileName)
    {
        PruneExpired();

        string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));

        tickets[token] = new Ticket(
            owner,
            sourceId,
            paths,
            fileName,
            timeProvider.GetUtcNow().Add(Lifetime));

        return token;
    }

    /// <summary>
    /// Redeems a ticket, or returns null when it is unknown, expired, already used, or belongs
    /// to a different owner.
    /// </summary>
    public Ticket? Redeem(string token, string owner)
    {
        if (!tickets.TryRemove(token, out var ticket))
        {
            return null;
        }

        if (ticket.ExpiresAt <= timeProvider.GetUtcNow())
        {
            return null;
        }

        // The permission checks downstream would catch a mismatched owner anyway; failing here
        // keeps a leaked ticket from even naming someone else's paths.
        return string.Equals(ticket.Owner, owner, StringComparison.Ordinal) ? ticket : null;
    }

    /// <summary>The owner value for a ticket created on behalf of a share visitor.</summary>
    public static string ShareOwner(string shareToken) => $"share:{shareToken}";

    /// <summary>The owner value for a signed-in caller.</summary>
    public static string UserOwner(string? userId) => $"user:{userId ?? "anonymous"}";

    private void PruneExpired()
    {
        var now = timeProvider.GetUtcNow();

        foreach (var (token, ticket) in tickets)
        {
            if (ticket.ExpiresAt <= now)
            {
                tickets.TryRemove(token, out _);
            }
        }
    }

    public sealed record Ticket(
        string Owner,
        int SourceId,
        IReadOnlyList<string> Paths,
        string FileName,
        DateTimeOffset ExpiresAt);
}
