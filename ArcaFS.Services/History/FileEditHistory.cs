namespace ArcaFS.Services.History;

/// <summary>Whether a history step applied. <see cref="Blocked"/> means the item changed and was left alone.</summary>
public readonly record struct HistoryOutcome(bool Succeeded, string? Error, bool Blocked)
{
    public static HistoryOutcome Ok() => new(true, null, false);

    public static HistoryOutcome Fail(string? error) => new(false, error, false);

    public static HistoryOutcome Refuse() => new(false, null, true);

    public static HistoryOutcome From(IResult result) =>
        result.Status is ResultStatus.Ok or ResultStatus.Created or ResultStatus.NoContent
            ? Ok()
            : Fail(result.GetDisplayMessage());

    public static HistoryOutcome FromBulk(Result<BulkOperationResultDto> result)
    {
        if (!result.IsSuccess)
        {
            return Fail(result.GetDisplayMessage());
        }

        if (result.Value.IsCompleteSuccess)
        {
            return Ok();
        }

        return Fail(result.Value.Results.FirstOrDefault(row => !row.Succeeded)?.Error);
    }
}

/// <summary>One reversible change. Undo and redo call the same service the original gesture did.</summary>
public abstract class HistoryAction
{
    public abstract Task<HistoryOutcome> UndoAsync(IFileSystemService service, CancellationToken cancellationToken = default);

    public abstract Task<HistoryOutcome> RedoAsync(IFileSystemService service, CancellationToken cancellationToken = default);
}

/// <summary>
/// Undo and redo of the changes made in this circuit. Delete is not on the stack: it asks for
/// confirmation, and putting the bytes back would need a trash this server does not have.
/// </summary>
public sealed class FileEditHistory
{
    public const int Limit = 50;

    public const int MaxUndoTextChars = 256 * 1024;

    // Lists, so the undo history can drop its oldest action past the limit. A stack only removes the newest one.
    private readonly List<HistoryAction> undo = [];
    private readonly List<HistoryAction> redo = [];

    public bool CanUndo => undo.Count > 0;

    public bool CanRedo => redo.Count > 0;

    public void Push(HistoryAction action)
    {
        undo.Add(action);
        if (undo.Count > Limit)
        {
            undo.RemoveAt(0);
        }

        redo.Clear();
    }

    public HistoryAction? TryUndo()
    {
        if (undo.Count == 0)
        {
            return null;
        }

        HistoryAction action = undo[^1];
        undo.RemoveAt(undo.Count - 1);
        redo.Add(action);
        return action;
    }

    public HistoryAction? TryRedo()
    {
        if (redo.Count == 0)
        {
            return null;
        }

        HistoryAction action = redo[^1];
        redo.RemoveAt(redo.Count - 1);
        undo.Add(action);
        return action;
    }

    /// <summary>Puts the action <see cref="TryUndo"/> just handed out back, because applying it failed.</summary>
    public void RevertUndo()
    {
        if (redo.Count == 0)
        {
            return;
        }

        undo.Add(redo[^1]);
        redo.RemoveAt(redo.Count - 1);
    }

    /// <summary>Puts the action <see cref="TryRedo"/> just handed out back, because applying it failed.</summary>
    public void RevertRedo()
    {
        if (undo.Count == 0)
        {
            return;
        }

        redo.Add(undo[^1]);
        undo.RemoveAt(undo.Count - 1);
    }
}
