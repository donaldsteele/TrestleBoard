using TrestleBoard.Core.Commands;
using TrestleBoard.Core.Model;
using Xunit;

namespace TrestleBoard.Core.Tests;

/// <summary>Deterministic clock for coalescing-window tests.</summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private long _timestamp;

    public override long GetTimestamp() => _timestamp;

    public void Advance(TimeSpan span) => _timestamp += (long)(span.TotalSeconds * TimestampFrequency);
}

public sealed class SessionTests
{
    private static (DocumentSession Session, ManualTimeProvider Clock) NewSession()
    {
        var clock = new ManualTimeProvider();
        return (new DocumentSession(Fixtures.BuildDocument(), clock), clock);
    }

    private static string ParagraphText(Document doc, int paragraphIndex = 0) =>
        StoryText.GetText(doc.GetStory("story-1").Paragraphs[paragraphIndex]);

    [Fact]
    public void KeystrokesWithinWindowCoalesceIntoOneUndo()
    {
        (DocumentSession session, ManualTimeProvider clock) = NewSession();
        string before = ParagraphText(session.Document, 1);

        int offset = 0;
        foreach (char c in "Hello")
        {
            clock.Advance(TimeSpan.FromMilliseconds(100));
            session.Execute(new InsertTextCommand("story-1", 1, offset++, c.ToString()));
        }

        Assert.Equal("Hello" + before, ParagraphText(session.Document, 1));
        session.Undo();
        Assert.Equal(before, ParagraphText(session.Document, 1));
        Assert.False(session.CanUndo);

        session.Redo();
        Assert.Equal("Hello" + before, ParagraphText(session.Document, 1));
    }

    [Fact]
    public void KeystrokesOutsideWindowStaySeparate()
    {
        (DocumentSession session, ManualTimeProvider clock) = NewSession();
        clock.Advance(TimeSpan.FromSeconds(5));
        session.Execute(new InsertTextCommand("story-1", 1, 0, "A"));
        clock.Advance(TimeSpan.FromSeconds(5));
        session.Execute(new InsertTextCommand("story-1", 1, 1, "B"));

        session.Undo();
        Assert.StartsWith("A", ParagraphText(session.Document, 1), StringComparison.Ordinal);
        Assert.True(session.CanUndo);
    }

    [Fact]
    public void BackspaceBurstCoalescesAndUndoesAsOne()
    {
        (DocumentSession session, ManualTimeProvider clock) = NewSession();
        string before = ParagraphText(session.Document, 1);

        // Backspace over the last three characters, newest deletion leftmost.
        for (int i = 1; i <= 3; i++)
        {
            clock.Advance(TimeSpan.FromMilliseconds(50));
            session.Execute(new DeleteTextCommand("story-1", 1, before.Length - i, 1));
        }

        Assert.Equal(before[..^3], ParagraphText(session.Document, 1));
        session.Undo();
        Assert.Equal(before, ParagraphText(session.Document, 1));
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void ExecuteClearsRedoStack()
    {
        (DocumentSession session, ManualTimeProvider clock) = NewSession();
        session.Execute(new MoveBlockCommand("img-1", new RectPt(1f, 2f, 180f, 120f)));
        session.Undo();
        Assert.True(session.CanRedo);

        clock.Advance(TimeSpan.FromSeconds(5));
        session.Execute(new MoveBlockCommand("img-1", new RectPt(9f, 9f, 180f, 120f)));
        Assert.False(session.CanRedo);
    }

    [Fact]
    public void UndoAfterUndoDoesNotMergeAcrossUndoBoundary()
    {
        (DocumentSession session, ManualTimeProvider clock) = NewSession();
        session.Execute(new InsertTextCommand("story-1", 1, 0, "A"));
        session.Undo();
        session.Redo();

        // Immediately typing after a redo must not merge into the redone command's window.
        clock.Advance(TimeSpan.FromMilliseconds(10));
        session.Execute(new InsertTextCommand("story-1", 1, 1, "B"));
        session.Undo();
        Assert.StartsWith("A", ParagraphText(session.Document, 1), StringComparison.Ordinal);
    }

    [Fact]
    public void ChangedRaisedWithScopeAndOrigin()
    {
        (DocumentSession session, _) = NewSession();
        var events = new List<(ChangeOrigin Origin, ChangeScope Scope)>();
        session.Changed += (_, e) => events.Add((e.Origin, e.Scope));

        session.Execute(new MoveBlockCommand("img-1", new RectPt(1f, 2f, 180f, 120f)));
        session.Undo();
        session.Redo();

        Assert.Equal(
            [ChangeOrigin.Execute, ChangeOrigin.Undo, ChangeOrigin.Redo],
            events.Select(e => e.Origin));
        Assert.All(events, e =>
        {
            Assert.Equal(ChangeKind.BlockGeometry, e.Scope.Kind);
            Assert.Equal("img-1", e.Scope.BlockId);
        });
    }

    [Fact]
    public void MergedCommandRedoReplaysWholeBurst()
    {
        (DocumentSession session, ManualTimeProvider clock) = NewSession();
        string before = ParagraphText(session.Document, 1);
        session.Execute(new InsertTextCommand("story-1", 1, 0, "Hi "));
        clock.Advance(TimeSpan.FromMilliseconds(100));
        session.Execute(new InsertTextCommand("story-1", 1, 3, "there "));

        session.Undo();
        Assert.Equal(before, ParagraphText(session.Document, 1));
        session.Redo();
        Assert.Equal("Hi there " + before, ParagraphText(session.Document, 1));
    }

    [Fact]
    public void UndoDescriptionsUsePlainLanguage()
    {
        (DocumentSession session, _) = NewSession();
        Assert.Null(session.UndoDescription);
        session.Execute(new MoveBlockCommand("img-1", new RectPt(1f, 2f, 180f, 120f)));
        Assert.Equal("Move frame", session.UndoDescription);
        session.Undo();
        Assert.Equal("Move frame", session.RedoDescription);
    }
}
