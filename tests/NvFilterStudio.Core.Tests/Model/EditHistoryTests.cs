using NvFilterStudio.Core.Model;

namespace NvFilterStudio.Core.Tests.Model;

public class EditHistoryTests
{
    private static EditSnapshot Snap(string json, string description = "a change") =>
        new(json, @"C:\Games\Example\Example.exe", 3, description);

    [Fact]
    public void NewHistory_HasNothingToUndoOrRedo()
    {
        var history = new EditHistory();

        Assert.False(history.CanUndo);
        Assert.False(history.CanRedo);
        Assert.Null(history.NextUndoDescription);
    }

    [Fact]
    public void Undo_ReturnsTheStateRecordedBeforeTheChange()
    {
        var history = new EditHistory();
        history.Record(Snap("before", "removing Details"));

        EditSnapshot? restored = history.Undo(Snap("after"));

        Assert.NotNull(restored);
        Assert.Equal("before", restored!.Json);
        Assert.False(history.CanUndo);
        Assert.True(history.CanRedo);
    }

    [Fact]
    public void RedoReturnsToWhereUndoStartedFrom()
    {
        var history = new EditHistory();
        history.Record(Snap("before"));

        EditSnapshot current = Snap("after");
        history.Undo(current);
        EditSnapshot? redone = history.Redo(Snap("before"));

        Assert.Equal("after", redone!.Json);
    }

    [Fact]
    public void UndoThenRedo_RoundTripsSeveralSteps()
    {
        var history = new EditHistory();
        history.Record(Snap("v1"));
        history.Record(Snap("v2"));
        history.Record(Snap("v3"));

        Assert.Equal("v3", history.Undo(Snap("v4"))!.Json);
        Assert.Equal("v2", history.Undo(Snap("v3"))!.Json);
        Assert.Equal("v3", history.Redo(Snap("v2"))!.Json);
        Assert.Equal("v4", history.Redo(Snap("v3"))!.Json);
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void RecordingAfterUndo_DropsTheForwardHistory()
    {
        // Standard editor behaviour: branching discards the abandoned future.
        var history = new EditHistory();
        history.Record(Snap("v1"));
        history.Undo(Snap("v2"));

        Assert.True(history.CanRedo);
        history.Record(Snap("v1b"));

        Assert.False(history.CanRedo);
    }

    [Fact]
    public void Undo_OnEmptyHistory_ReturnsNull()
    {
        Assert.Null(new EditHistory().Undo(Snap("current")));
    }

    [Fact]
    public void Redo_WithNothingUndone_ReturnsNull()
    {
        var history = new EditHistory();
        history.Record(Snap("v1"));

        Assert.Null(history.Redo(Snap("current")));
    }

    [Fact]
    public void History_IsBoundedAndDropsTheOldest()
    {
        // Each step is a copy of the document, so the cap is a memory bound.
        var history = new EditHistory();
        for (int i = 0; i <= EditHistory.Capacity + 5; i++)
        {
            history.Record(Snap($"v{i}"));
        }

        int steps = 0;
        var current = Snap("head");
        while (history.Undo(current) is { } previous)
        {
            current = previous;
            steps++;
        }

        Assert.Equal(EditHistory.Capacity, steps);
    }

    [Fact]
    public void NextUndoDescription_NamesTheChangeThatWouldBeReversed()
    {
        var history = new EditHistory();
        history.Record(Snap("before", "pasting a share code"));

        Assert.Equal("pasting a share code", history.NextUndoDescription);
    }

    [Fact]
    public void Clear_ForgetsBothDirections()
    {
        var history = new EditHistory();
        history.Record(Snap("v1"));
        history.Undo(Snap("v2"));

        history.Clear();

        Assert.False(history.CanUndo);
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void NextRedoDescription_NamesTheActionThatWouldBeReapplied()
    {
        // The caller labels the state it pushes onto the redo stack with this,
        // so the status line can say "Redid your slider changes" rather than
        // repeating whatever placeholder the caller invented - it used to read
        // "Redid redo".
        var history = new EditHistory();
        history.Record(Snap("before", "your slider changes"));

        history.Undo(Snap("after", "your slider changes"));

        Assert.Equal("your slider changes", history.NextRedoDescription);
    }

    [Fact]
    public void NextRedoDescription_IsNullWithNothingToRedo()
    {
        var history = new EditHistory();
        history.Record(Snap("before"));

        Assert.Null(history.NextRedoDescription);
    }

    [Fact]
    public void UndoThenRedo_ReturnsTheDocumentToWhereItStarted()
    {
        // The round trip the UI depends on. A snapshot has to be recorded from
        // the state *before* a change; recording it afterwards returns the very
        // edit undo was meant to remove. That ordering lives in the caller, but
        // this pins the mechanism it relies on.
        var history = new EditHistory();
        history.Record(Snap("before", "an edit"));

        EditSnapshot? undone = history.Undo(Snap("after", "an edit"));
        Assert.Equal("before", undone!.Json);

        EditSnapshot? redone = history.Redo(Snap("before", "an edit"));
        Assert.Equal("after", redone!.Json);
        Assert.Equal("an edit", redone.Description);
    }
}
