using System.Collections.Concurrent;

namespace ArcaFS.Infrastructure;

/// <summary>
/// Transient notifications, rendered once by <c>Toaster</c> in the layout.
/// </summary>
/// <remarks>
/// Bulk file operations succeed partially by design, so results need somewhere to go that isn't a
/// blocking dialog. Scoped to the circuit, so one user's toasts stay theirs.
/// </remarks>
public sealed class ToastService : IDisposable
{
    private readonly ConcurrentDictionary<Guid, Toast> toasts = new();
    private readonly ConcurrentDictionary<Guid, Timer> timers = new();

    public event Action? Changed;

    public IReadOnlyCollection<Toast> Toasts =>
        toasts.Values.OrderBy(t => t.CreatedAt).ToList();

    public void Success(string message) => Show(message, "success");

    public void Error(string message) => Show(message, "danger", TimeSpan.FromSeconds(12));

    public void Warning(string message) => Show(message, "warning", TimeSpan.FromSeconds(9));

    public void Info(string message) => Show(message, "info");

    /// <summary>
    /// Summarises a bulk result: one line when everything worked, otherwise a count plus the
    /// first failure, which is nearly always representative of the rest.
    /// </summary>
    public void ShowBulkResult(BulkOperationResultDto result, string verb)
    {
        if (result.IsCompleteSuccess)
        {
            Success($"{verb} {result.SucceededCount} {(result.SucceededCount == 1 ? "item" : "items")}.");
            return;
        }

        string? firstError = result.Results.FirstOrDefault(r => !r.Succeeded)?.Error;

        if (result.SucceededCount == 0)
        {
            Error($"Could not {verb.ToLowerInvariant()} anything. {firstError}");
            return;
        }

        Warning(
            $"{verb} {result.SucceededCount} of {result.SucceededCount + result.FailedCount} items. " +
            $"{result.FailedCount} failed: {firstError}");
    }

    public void Dismiss(Guid id)
    {
        toasts.TryRemove(id, out _);

        if (timers.TryRemove(id, out var timer))
        {
            timer.Dispose();
        }

        Changed?.Invoke();
    }

    private void Show(string message, string level, TimeSpan? duration = null)
    {
        var toast = new Toast(Guid.NewGuid(), message, level, DateTime.UtcNow);
        toasts[toast.Id] = toast;

        var timer = new Timer(
            _ => Dismiss(toast.Id),
            null,
            duration ?? TimeSpan.FromSeconds(6),
            Timeout.InfiniteTimeSpan);

        timers[toast.Id] = timer;

        Changed?.Invoke();
    }

    public void Dispose()
    {
        foreach (var timer in timers.Values)
        {
            timer.Dispose();
        }

        timers.Clear();
        toasts.Clear();
    }

    public sealed record Toast(Guid Id, string Message, string Level, DateTime CreatedAt);
}
