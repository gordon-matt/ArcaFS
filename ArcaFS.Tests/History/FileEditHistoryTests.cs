using ArcaFS.Services.FileSystem;
using ArcaFS.Services.History;

namespace ArcaFS.Tests.History;

public class FileEditHistoryTests
{
    [Fact]
    public void Push_ClearsRedo()
    {
        var history = new FileEditHistory();
        history.Push(new Marker(1));
        history.TryUndo();

        history.Push(new Marker(2));

        Assert.False(history.CanRedo);
        Assert.Equal(2, ((Marker)history.TryUndo()!).Id);
    }

    [Fact]
    public void TryUndo_MovesTheActionOntoRedo()
    {
        var history = new FileEditHistory();
        history.Push(new Marker(1));

        HistoryAction? undone = history.TryUndo();

        Assert.Equal(1, ((Marker)undone!).Id);
        Assert.False(history.CanUndo);
        Assert.True(history.CanRedo);
        Assert.Equal(1, ((Marker)history.TryRedo()!).Id);
    }

    [Fact]
    public void RevertUndo_RestoresAFailedStep()
    {
        var history = new FileEditHistory();
        history.Push(new Marker(1));
        history.TryUndo();

        history.RevertUndo();

        Assert.True(history.CanUndo);
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void Push_DropsTheOldestPastTheLimit()
    {
        var history = new FileEditHistory();
        for (int i = 0; i < FileEditHistory.Limit + 3; i++)
        {
            history.Push(new Marker(i));
        }

        var left = new List<int>();
        while (history.TryUndo() is Marker marker)
        {
            left.Add(marker.Id);
        }

        Assert.Equal(FileEditHistory.Limit, left.Count);
        Assert.Equal(FileEditHistory.Limit + 2, left[0]);
        Assert.DoesNotContain(0, left);
    }

    private sealed class Marker(int id) : HistoryAction
    {
        public int Id { get; } = id;

        public override Task<HistoryOutcome> UndoAsync(IFileSystemService service, CancellationToken cancellationToken = default) =>
            Task.FromResult(HistoryOutcome.Ok());

        public override Task<HistoryOutcome> RedoAsync(IFileSystemService service, CancellationToken cancellationToken = default) =>
            Task.FromResult(HistoryOutcome.Ok());
    }
}
