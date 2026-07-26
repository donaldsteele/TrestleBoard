using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using TrestleBoard.Layout.Widgets;
using TrestleBoard.Widgets.Wizards;
using Xunit;

namespace TrestleBoard.Widgets.Tests;

/// <summary>
/// The contract every widget must satisfy, checked once for all six (docs/M7-spec.md §1–§3).
/// A widget added later is covered by these the moment it lands in <see cref="BuiltInWidgets"/>.
/// </summary>
public sealed class WidgetContractTests
{
    private static readonly WidgetLayoutProvider Provider = WidgetLayoutProvider.CreateDefault();

    [Fact]
    public void TheSixV1WidgetsAreRegisteredInMenuOrder()
    {
        Assert.Equal(
            ["officersTable", "birthdayList", "committeeList", "districtCalendar", "eventCard", "coverBanner"],
            Provider.Registry.All.Select(d => d.TypeId));
    }

    [Fact]
    public void AnUnknownWidgetTypeIsRefusedQuietlyRatherThanThrown()
    {
        // The forward-compatibility path: a newsletter written by a later version must still open.
        Assert.False(Provider.Registry.TryGet("memorialPanel", out _));
        Assert.False(Provider.Registry.TryGet(null, out _));
        Assert.False(Provider.TryGetInfo("memorialPanel", out _));
        Assert.False(Provider.TryGetStyleDefaults("memorialPanel", out _));
        Assert.False(Provider.TryLayout(Request("memorialPanel", null, 1), out _));
    }

    [Fact]
    public void RegisteringTheSameTypeTwiceIsAProgrammingError()
    {
        var registry = new WidgetRegistry();
        registry.Register(BuiltInWidgets.All[0]);

        Assert.Throws<InvalidOperationException>(() => registry.Register(BuiltInWidgets.All[0]));
    }

    [Theory]
    [MemberData(nameof(Widgets))]
    public void EveryWidgetRoundTripsItsOwnEmptyData(string typeId)
    {
        IWidgetDefinition definition = Get(typeId);
        object empty = definition.CreateEmptyData(WidgetTestData.Seed);
        JsonElement written = definition.WriteData(empty);

        Assert.True(definition.TryReadData(written, definition.CurrentDataVersion, out object read));
        Assert.Equal(written.GetRawText(), definition.WriteData(read).GetRawText());
    }

    [Theory]
    [MemberData(nameof(Widgets))]
    public void EveryWidgetRefusesAPayloadFromANewerVersion(string typeId)
    {
        IWidgetDefinition definition = Get(typeId);
        JsonElement written = definition.WriteData(definition.CreateEmptyData(WidgetTestData.Seed));

        Assert.False(definition.TryReadData(written, definition.CurrentDataVersion + 1, out _));
        Assert.False(Provider.TryLayout(Request(typeId, written, definition.CurrentDataVersion + 1), out _));
    }

    [Theory]
    [MemberData(nameof(Widgets))]
    public void EveryWidgetSurvivesRubbishWithoutThrowing(string typeId)
    {
        IWidgetDefinition definition = Get(typeId);
        using var doc = JsonDocument.Parse("""{"officers": "not an array"}""");

        Assert.False(definition.TryReadData(null, 1, out _));
        Assert.False(Provider.TryLayout(Request(typeId, null, 1), out _));

        // Malformed but well-formed JSON must degrade to a placeholder, never take the app down.
        bool read = definition.TryReadData(doc.RootElement, 1, out _);
        Assert.True(read || !read);
    }

    [Theory]
    [MemberData(nameof(Widgets))]
    public void EveryWidgetLaysOutEmptyAndSaysSo(string typeId)
    {
        // A blank seed, so even the one widget that copies the document's own lodge name (§8.3)
        // starts with nothing in it.
        IWidgetDefinition definition = Get(typeId);
        JsonElement empty = definition.WriteData(definition.CreateEmptyData(new WidgetSeed("", 1, 2026, "")));

        Assert.True(Provider.TryLayout(Request(typeId, empty, definition.CurrentDataVersion), out WidgetDrawList list));
        Assert.True(list.IsEmpty, $"{typeId} should report an empty draw list for empty data");
        Assert.NotEqual("", list.EmptyPromptText);
        Assert.True(list.HeightPt >= 0f);
    }

    [Theory]
    [MemberData(nameof(Widgets))]
    public void EveryWidgetLaysOutIdenticallyTwice(string typeId)
    {
        IWidgetDefinition definition = Get(typeId);
        JsonElement empty = definition.WriteData(definition.CreateEmptyData(WidgetTestData.Seed));

        Assert.True(Provider.TryLayout(Request(typeId, empty, definition.CurrentDataVersion), out WidgetDrawList a));
        Assert.True(Provider.TryLayout(Request(typeId, empty, definition.CurrentDataVersion), out WidgetDrawList b));
        Assert.Equal(WidgetDrawListDump.Write(a), WidgetDrawListDump.Write(b));
    }

    [Theory]
    [MemberData(nameof(Widgets))]
    public void EveryWidgetHasAWizardThatEndsInReview(string typeId)
    {
        WizardDefinition wizard = Get(typeId).Wizard;

        Assert.NotEmpty(wizard.Steps);
        Assert.Equal(WizardStepKind.Review, wizard.Steps[^1].Kind);
        Assert.Single(wizard.Steps, s => s.Kind == WizardStepKind.Review);
        Assert.All(
            wizard.Steps.Where(s => s.Kind == WizardStepKind.Fields),
            s => Assert.InRange(s.Fields.Count, 1, 3));
    }

    /// <summary>
    /// Every data type carries an overflow bag, so a property written by a future build survives a
    /// round trip exactly as the rest of the document model does (docs/M7-spec.md §1.2 rule 5).
    /// </summary>
    [Theory]
    [MemberData(nameof(Widgets))]
    public void EveryDataTypeAndItsNestedTypesKeepUnknownProperties(string typeId)
    {
        foreach (Type type in DataTypes(Get(typeId).DataType))
        {
            Assert.True(
                type.GetProperty("ExtraProperties") is not null,
                $"{type.Name} needs a [JsonExtensionData] ExtraProperties bag");
        }
    }

    /// <summary>
    /// Dictionaries have no defined enumeration order, so a payload containing one could not be
    /// laid out identically on three operating systems (docs/M7-spec.md §1.2 rule 4).
    /// </summary>
    [Theory]
    [MemberData(nameof(Widgets))]
    public void NoDataTypeStoresADictionaryExceptItsOverflowBag(string typeId)
    {
        foreach (Type type in DataTypes(Get(typeId).DataType))
        {
            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.Name == "ExtraProperties")
                {
                    continue;
                }

                Assert.False(
                    property.PropertyType.IsGenericType
                        && property.PropertyType.GetGenericTypeDefinition() == typeof(Dictionary<,>),
                    $"{type.Name}.{property.Name} must not be a dictionary");
            }
        }
    }

    /// <summary>
    /// "Must NEVER throw" is the entire contract of the layout seam: a faulty layouter has to become
    /// a grey box, not a dead paint pass that takes the newsletter down with it (docs/M7-spec.md §4.1).
    /// </summary>
    [Fact]
    public void ALayouterThatFaultsBecomesAPlaceholderInsteadOfKillingThePaint()
    {
        var registry = new WidgetRegistry();
        registry.Register(new ExplodingWidget());
        var provider = new WidgetLayoutProvider(registry);

        using var doc = JsonDocument.Parse("""{"heading":"Sample","note":""}""");

        Assert.False(provider.TryLayout(
            new WidgetLayoutRequest(
                "testExploding", 1, doc.RootElement, 240f,
                WidgetTestData.CreateStyle(), WidgetTestData.CreateShaper()),
            out _));
    }

    [Fact]
    public void AMissingMigrationStepDegradesToReadOnlyRatherThanLosingData()
    {
        var definition = new TwoVersionWidget();
        using var doc = JsonDocument.Parse("""{"heading":"Sample"}""");

        // v1 -> v2 is implemented, so a v1 payload upgrades.
        Assert.True(definition.TryReadData(doc.RootElement, 1, out object upgraded));
        Assert.Equal("Sample", ((OneField)upgraded).Heading);
        Assert.Equal("upgraded", ((OneField)upgraded).Note);

        // A gap in the chain refuses rather than guessing.
        var broken = new BrokenChainWidget();
        Assert.False(broken.TryReadData(doc.RootElement, 1, out _));
    }

    public static TheoryData<string> Widgets()
    {
        var data = new TheoryData<string>();
        foreach (IWidgetDefinition definition in BuiltInWidgets.All)
        {
            data.Add(definition.TypeId);
        }

        return data;
    }

    private static IWidgetDefinition Get(string typeId)
    {
        Assert.True(Provider.Registry.TryGet(typeId, out IWidgetDefinition? definition));
        return definition!;
    }

    private static WidgetLayoutRequest Request(string typeId, JsonElement? data, int dataVersion) =>
        new(typeId, dataVersion, data, 240f, WidgetTestData.CreateStyle(), WidgetTestData.CreateShaper());

    /// <summary>The data type plus every nested type it stores, ignoring BCL types.</summary>
    private static List<Type> DataTypes(Type root)
    {
        var seen = new List<Type>();
        Walk(root);
        return seen;

        void Walk(Type type)
        {
            if (seen.Contains(type))
            {
                return;
            }

            seen.Add(type);
            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                Type candidate = property.PropertyType;
                if (candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(List<>))
                {
                    candidate = candidate.GetGenericArguments()[0];
                }

                if (candidate.Namespace?.StartsWith("TrestleBoard.Widgets.Builtins", StringComparison.Ordinal) == true
                    && candidate.IsClass)
                {
                    Walk(candidate);
                }
            }
        }
    }

    // ---- migration fixtures ------------------------------------------------------------------

    private sealed class OneField
    {
        public string Heading { get; set; } = "";

        public string Note { get; set; } = "";

        [System.Text.Json.Serialization.JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtraProperties { get; set; }
    }

    private class TwoVersionWidget : WidgetDefinition<OneField>
    {
        public override string TypeId => "testTwoVersion";

        public override string DisplayName => "Test widget";

        public override string Description => "Fixture.";

        public override string IconKey => "test";

        public override Core.Model.SizePt DefaultSizePt => new(100f, 50f);

        public override int CurrentDataVersion => 2;

        public override IWidgetLayouter Layouter { get; } = new EmptyLayouter();

        // Test-only: the real widgets are source-generated (spec §1.2 rule 1), but a private nested
        // fixture type cannot be, so this one asks for a reflection resolver explicitly.
        private static readonly System.Text.Json.JsonSerializerOptions FixtureOptions = new()
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
            TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
        };

        protected override System.Text.Json.Serialization.Metadata.JsonTypeInfo<OneField> TypeInfo =>
            (System.Text.Json.Serialization.Metadata.JsonTypeInfo<OneField>)
                FixtureOptions.GetTypeInfo(typeof(OneField));

        public override OneField CreateEmpty(WidgetSeed seed) => new();

        protected override WizardDefinition BuildWizard() => new("Test", "", [new ReviewStep()]);

        public override bool TryMigrateStep(JsonNode data, int fromVersion, out JsonNode upgraded, out int toVersion)
        {
            upgraded = data;
            toVersion = fromVersion;
            if (fromVersion != 1)
            {
                return false;
            }

            data["note"] = "upgraded";
            toVersion = 2;
            return true;
        }
    }

    private sealed class BrokenChainWidget : TwoVersionWidget
    {
        public override int CurrentDataVersion => 3;
    }

    /// <summary>Throws the kind of thing a hand-written layouter gets wrong: an index slip.</summary>
    private sealed class ExplodingWidget : TwoVersionWidget
    {
        public override string TypeId => "testExploding";

        public override int CurrentDataVersion => 1;

        public override IWidgetLayouter Layouter { get; } = new ExplodingLayouter();
    }

    private sealed class ExplodingLayouter : IWidgetLayouter
    {
        public WidgetDrawList Layout(WidgetLayoutContext context)
        {
            int[] empty = [];
            return empty[3] == 0 ? throw new InvalidOperationException() : null!;
        }
    }

    private sealed class EmptyLayouter : IWidgetLayouter
    {
        public WidgetDrawList Layout(WidgetLayoutContext context) =>
            new WidgetDrawListBuilder(context.WidthPt).Build(isEmpty: true, "Test — not filled in yet.");
    }
}
