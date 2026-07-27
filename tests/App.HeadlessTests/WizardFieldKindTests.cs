using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using TrestleBoard.App.Dialogs;
using TrestleBoard.Core.Model;
using TrestleBoard.Layout.Widgets;
using TrestleBoard.Widgets;
using TrestleBoard.Widgets.Wizards;
using Xunit;

namespace TrestleBoard.App.HeadlessTests;

/// <summary>
/// Every <see cref="WizardFieldKind"/> gets the control it was invented for, in BOTH windows
/// (PLAN.md §11 M13). The failure this exists to catch is silent: a kind nobody wrote a case for
/// falls through to a plain text box, and the wizard keeps working while quietly offering none of
/// the help the field kind promised. The grid editor is a second view of the same wizard, not a
/// laxer one, so it is held to the same table.
/// </summary>
public sealed class WizardFieldKindTests
{
    private static HeadlessUnitTestSession Session => HeadlessSession.Instance;

    /// <summary>
    /// The table both windows are measured against. Adding a kind without adding a row here fails
    /// <see cref="TheTableCoversEveryFieldKindThereIs"/> immediately, by name.
    /// </summary>
    private static readonly Dictionary<WizardFieldKind, Type> Expected = new()
    {
        [WizardFieldKind.Text] = typeof(TextBox),
        [WizardFieldKind.MultiLineText] = typeof(TextBox),
        [WizardFieldKind.Phone] = typeof(TextBox),
        [WizardFieldKind.MonthDay] = typeof(TextBox),
        [WizardFieldKind.DayOfMonthRule] = typeof(TextBox),
        [WizardFieldKind.Time] = typeof(TextBox),
        [WizardFieldKind.Choice] = typeof(ComboBox),
        [WizardFieldKind.Person] = typeof(AutoCompleteBox),
    };

    private static readonly PersonSuggestion[] People =
    [
        new("m-1", "A. Placeholder", "555-0100"),
        new("m-2", "B. Sample", null),
    ];

    [Fact]
    public void TheTableCoversEveryFieldKindThereIs()
    {
        List<string> missing = [.. Enum.GetValues<WizardFieldKind>()
            .Where(kind => !Expected.ContainsKey(kind))
            .Select(kind => kind.ToString())];

        Assert.True(missing.Count == 0, "no control type is declared for: " + string.Join(", ", missing));
    }

    [Fact]
    public async Task TheStepWizardRendersEveryFieldKindWithItsOwnControl()
    {
        await Session.Dispatch(() =>
        {
            foreach ((WizardFieldKind kind, Type expected) in Expected)
            {
                var window = new WizardWindow(NewSession(kind), People);
                window.RenderForTest();

                Control input = FindInput(window.ScreenControlsForTest, kind);
                Assert.True(
                    expected.IsInstanceOfType(input),
                    $"{kind} rendered a {input.GetType().Name} in the wizard, not a {expected.Name}");
                AssertMultiLineIsActuallyMultiLine(kind, input);
            }
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task TheGridEditorRendersEveryFieldKindWithItsOwnControl()
    {
        await Session.Dispatch(() =>
        {
            foreach ((WizardFieldKind kind, Type expected) in Expected)
            {
                var window = new WidgetGridWindow(NewSession(kind), People);
                window.RenderForTest();

                Control input = FindInput(window.ScreenControlsForTest, kind);
                Assert.True(
                    expected.IsInstanceOfType(input),
                    $"{kind} rendered a {input.GetType().Name} in the grid editor, not a {expected.Name}");
                AssertMultiLineIsActuallyMultiLine(kind, input);
            }
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task APersonFieldStillTakesANameThatIsNotInTheAddressBook()
    {
        await Session.Dispatch(() =>
        {
            // Free typing is the rule that stops the picker becoming a dead end (PLAN.md §11 M13).
            WizardSession session = NewSession(WizardFieldKind.Person);
            var window = new WizardWindow(session, People);
            window.RenderForTest();

            var picker = (AutoCompleteBox)FindInput(window.ScreenControlsForTest, WizardFieldKind.Person);
            picker.Text = "Z. Visitor";

            Assert.Equal("Z. Visitor", session.GetValue("answer"));
        }, TestContext.Current.CancellationToken);
    }

    // ---- fixtures ------------------------------------------------------------------------------

    private static void AssertMultiLineIsActuallyMultiLine(WizardFieldKind kind, Control input)
    {
        if (input is TextBox box)
        {
            Assert.Equal(kind == WizardFieldKind.MultiLineText, box.AcceptsReturn);
        }
    }

    private static Control FindInput(IReadOnlyList<Control> controls, WizardFieldKind kind)
    {
        List<Control> matches = [.. controls.Where(c => c is TextBox or ComboBox or AutoCompleteBox)];

        Assert.True(matches.Count > 0, $"{kind} produced no input control at all");
        return Assert.Single(matches);
    }

    private static WizardSession NewSession(WizardFieldKind kind) =>
        WizardSession.Create(new OneFieldWidget(kind), null, 1, new WidgetSeed("Sample Lodge 000", 7, 2026, "1st Tuesday"));

    private sealed class OneField
    {
        public string Answer { get; set; } = "";

        [System.Text.Json.Serialization.JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtraProperties { get; set; }
    }

    /// <summary>A wizard of exactly one question, so the window under test renders exactly one input.</summary>
    private sealed class OneFieldWidget(WizardFieldKind kind) : WidgetDefinition<OneField>
    {
        private static readonly JsonSerializerOptions FixtureOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        };

        public override string TypeId => "testOneField";

        public override string DisplayName => "Test";

        public override string Description => "Fixture.";

        public override string IconKey => "test";

        public override SizePt DefaultSizePt => new(100f, 50f);

        public override IWidgetLayouter Layouter { get; } = new NoLayouter();

        protected override JsonTypeInfo<OneField> TypeInfo =>
            (JsonTypeInfo<OneField>)FixtureOptions.GetTypeInfo(typeof(OneField));

        public override OneField CreateEmpty(WidgetSeed seed) => new();

        protected override WizardDefinition BuildWizard() => new(
            "Test",
            "One question, so the window renders one control.",
            [
                new FieldsStep<OneField>(
                    "The question",
                    null,
                    [
                        (new WizardField(
                                "answer",
                                "Answer",
                                kind,
                                IsOptional: true,
                                Choices: kind == WizardFieldKind.Choice
                                    ? [new WizardChoice("a", "The first one"), new WizardChoice("b", "The second")]
                                    : null,
                                AllowsPeoplePicker: kind == WizardFieldKind.MultiLineText),
                            new WizardFieldBinding<OneField>(
                                "answer", d => d.Answer, (d, v) => d.Answer = v)),
                    ]),
                new ReviewStep(),
            ]);
    }

    private sealed class NoLayouter : IWidgetLayouter
    {
        public WidgetDrawList Layout(WidgetLayoutContext context) =>
            new WidgetDrawListBuilder(context.WidthPt).Build(isEmpty: true, "Test — not filled in yet.");
    }
}
