using System.Text.Json;
using TrestleBoard.Core.Commands;
using TrestleBoard.Core.Model;
using Xunit;

namespace TrestleBoard.Core.Tests;

/// <summary>
/// PLAN.md §8: Apply;Revert == identity for 100% of command types, verified by canonical
/// JSON snapshot. Redo correctness gets a second Apply→Revert cycle on the same instance.
/// </summary>
public sealed class CommandTests
{
    public static TheoryData<string> CommandFactories => new(CommandsUnderTest.Keys);

    private static readonly Dictionary<string, Func<Document, IDocumentCommand>> CommandsUnderTest = new()
    {
        ["InsertText"] = _ => new InsertTextCommand("story-1", 0, 5, "inserted words "),
        ["InsertText.ParagraphStart"] = _ => new InsertTextCommand("story-1", 1, 0, "Lead-in. "),
        ["DeleteText.WithinRun"] = _ => new DeleteTextCommand("story-1", 0, 2, 6),
        ["DeleteText.AcrossRuns"] = _ => new DeleteTextCommand("story-1", 0, 30, 20),
        ["MoveBlock"] = _ => new MoveBlockCommand("img-1", new RectPt(100f, 200f, 180f, 120f)),
        ["ResizeBlock"] = _ => new ResizeBlockCommand("text-1", new RectPt(54f, 90f, 400f, 250f)),
        ["SetWrapMode"] = _ => new SetWrapModeCommand("img-1", WrapMode.None, 0f),
        ["AddBlock"] = _ => new AddBlockCommand("page-2", new ShapeBlock
        {
            Id = "rule-new",
            Kind = ShapeKind.Rule,
            FrameRect = new RectPt(54f, 500f, 504f, 1f),
        }),
        ["RemoveBlock"] = _ => new RemoveBlockCommand("page-1", "rule-1"),
        ["SetImageRecipe"] = _ => new SetImageRecipeCommand("img-1", new ImageRecipe { Brightness = 0.2f }),
        // M18. A swap replaces four things at once — bytes, recipe, description and caption — so the
        // identity check is what proves one Ctrl+Z puts all four back.
        ["ReplaceImage"] = _ => new ReplaceImageCommand(
            "img-1", "img-2.jpg", "Brothers at the picnic", "A warm evening at the lodge."),
        ["SetPictureWords.AltText"] = _ => SetPictureWordsCommand.ForAltText("img-1", "A new description"),
        ["SetPictureWords.Caption"] = _ => SetPictureWordsCommand.ForCaption("img-1", "A new caption"),
        ["SetWidgetData"] = _ => new SetWidgetDataCommand(
            "widget-1",
            JsonSerializer.Deserialize<JsonElement>("""{"officers":[]}"""),
            2),
        ["AddPage"] = _ => new AddPageCommand(new Page { Id = "page-3", MasterRef = "master-1" }, 2),
        ["RemovePage"] = _ => new RemovePageCommand("page-2"),
        ["MovePage"] = _ => new MovePageCommand("page-2", 0),
        ["AddStory"] = _ => new AddStoryCommand(new Story { Id = "story-2" }),
        ["RemoveStory"] = _ => new RemoveStoryCommand("story-1"),
        ["SetMetadata"] = _ => new SetMetadataCommand(new DocumentMetadata { LodgeName = "Renamed Lodge", IssueMonth = 8, IssueYear = 2026 }),
        ["SplitParagraph"] = _ => new SplitParagraphCommand("story-1", 0, 10),
        ["SplitParagraph.MidRunBoundary"] = _ => new SplitParagraphCommand("story-1", 0, 35),
        ["MergeParagraph"] = _ => new MergeParagraphCommand("story-1", 0),
        ["ApplyCharacterStyle"] = _ => new ApplyCharacterStyleCommand("story-1", 0, 5, 20, "body"),
        ["ApplyCharacterStyle.Clear"] = _ => new ApplyCharacterStyleCommand("story-1", 0, 0, 10, null),
        ["ApplyParagraphStyle"] = _ => new ApplyParagraphStyleCommand("story-1", 0, "body-tight"),
        ["Composite"] = _ => new CompositeCommand(
            "Replace text",
            new ChangeScope(ChangeKind.Text, StoryId: "story-1"),
            [
                new DeleteTextCommand("story-1", 1, 0, 6),
                new InsertTextCommand("story-1", 1, 0, "Supper"),
            ]),
        // M7's widget commit: data and fit-to-contents in ONE undo step (docs/M7-spec.md §7.3).
        ["Composite.WidgetEdit"] = _ => new CompositeCommand(
            "Edit lodge officers",
            new ChangeScope(ChangeKind.BlockContent, BlockId: "widget-1"),
            [
                new SetWidgetDataCommand(
                    "widget-1",
                    JsonSerializer.Deserialize<JsonElement>(
                        """{"heading":"Lodge Officers","officers":[{"position":"Worshipful Master","name":"A. Placeholder"}]}"""),
                    1),
                new ResizeBlockCommand("widget-1", new RectPt(300f, 400f, 240f, 190f)),
            ]),
        ["SetZOrder"] = _ => new SetZOrderCommand("img-1", 0),
        ["SetLinkNext.Clear"] = _ => new SetLinkNextCommand("text-1", null),
        ["SetLinkNext.Set"] = _ => new SetLinkNextCommand("text-2", "text-1"),
        ["SetStoryRef"] = _ => new SetStoryRefCommand("text-2", "story-detached"),
        ["EnsureCharacterStyle"] = _ => new EnsureCharacterStyleCommand(new CharacterStyleDef
        {
            Name = "caption",
            FontFamily = "Source Sans 3",
            SizePt = 10f,
        }),
        // M14's sibling sweep: the fixture has "body" AND "body-bold" at the same family and
        // size, so both of these genuinely touch more than one style definition.
        ["SetCharacterStyleFont.Family"] = _ => new SetCharacterStyleFontCommand("body", "Lora", null),
        ["SetCharacterStyleFont.SizeOnly"] = _ => new SetCharacterStyleFontCommand("body", null, 14f),
    };

    [Theory]
    [MemberData(nameof(CommandFactories))]
    public void ApplyRevertIsIdentity(string key)
    {
        Document doc = Fixtures.BuildDocument();
        string before = Fixtures.Snapshot(doc);
        IDocumentCommand command = CommandsUnderTest[key](doc);

        command.Apply(doc);
        string applied = Fixtures.Snapshot(doc);
        Assert.NotEqual(before, applied);

        command.Revert(doc);
        Assert.Equal(before, Fixtures.Snapshot(doc));

        // Redo cycle: Apply must re-capture pre-state on every call.
        command.Apply(doc);
        Assert.Equal(applied, Fixtures.Snapshot(doc));
        command.Revert(doc);
        Assert.Equal(before, Fixtures.Snapshot(doc));
    }

    [Fact]
    public void EveryCommandTypeHasIdentityCoverage()
    {
        IEnumerable<Type> commandTypes = typeof(IDocumentCommand).Assembly.GetTypes()
            .Where(t => typeof(IDocumentCommand).IsAssignableFrom(t) && t is { IsAbstract: false, IsInterface: false });
        Document probe = Fixtures.BuildDocument();
        var covered = CommandsUnderTest.Values.Select(f => f(probe).GetType()).ToHashSet();
        foreach (Type type in commandTypes)
        {
            Assert.Contains(type, covered);
        }
    }
    /// <summary>
    /// Review §14.2: a composite that fails part way through leaves the document as it found it.
    ///
    /// <para>A composite is the app's unit of "one undoable thing". If a child throws, the ones
    /// before it have already mutated the document — and <c>DocumentSession.Execute</c> never
    /// reaches the line that pushes the command onto the undo stack, so the half-applied change
    /// could not be taken back: as far as the stack was concerned it never happened.</para>
    /// </summary>
    [Fact]
    public void ACompositeThatFailsPartWayThroughUndoesWhatItAlreadyDid()
    {
        Document document = Fixtures.BuildDocument();
        string before = Fixtures.Snapshot(document);

        var session = new DocumentSession(document);
        var composite = new CompositeCommand(
            "Half a change",
            new ChangeScope(ChangeKind.PageStructure),
            [
                new SetLinkNextCommand("text-1", null),
                new ThrowingCommand(),
            ]);

        Assert.Throws<InvalidOperationException>(() => session.Execute(composite));

        // Nothing the first child did survives, and nothing is on the undo stack to suggest it did.
        Assert.Equal(before, Fixtures.Snapshot(document));
        Assert.False(session.CanUndo);
    }

    /// <summary>A child that always fails, for the composite rollback test above.</summary>
    private sealed class ThrowingCommand : IDocumentCommand
    {
        public string Description => "Always fails";

        public ChangeScope Scope => new(ChangeKind.PageStructure);

        public void Apply(Document document) => throw new InvalidOperationException("no");

        public void Revert(Document document) => throw new InvalidOperationException("no");

        public bool TryMerge(IDocumentCommand newer) => false;
    }

}
