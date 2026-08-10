using System.Globalization;
using System.Text.Json.Serialization.Metadata;
using TrestleBoard.Core.Model;
using TrestleBoard.Layout.Widgets;
using TrestleBoard.Widgets.Wizards;

namespace TrestleBoard.Widgets.Builtins.CoverBanner;

/// <summary>
/// The front-page banner. The one widget that reads the seed at insert (docs/M7-spec.md §8.3): the
/// lodge's own name and its own meeting rule are not "a person's data" in the PLAN.md §0 sense — they
/// are the document's own metadata, already on file, so copying them here saves a re-typing that would
/// otherwise fall on an elderly volunteer every single month.
/// </summary>
public sealed class CoverBannerDefinition : WidgetDefinition<CoverBannerData>
{
    /// <summary>M75: the wizard field holding which month this issue is for.</summary>
    public const string IssueMonthFieldKey = "issueMonth";

    /// <summary>M75: the wizard field holding which year this issue is for.</summary>
    public const string IssueYearFieldKey = "issueYear";

    /// <summary>M75 (d): the field whose answer belongs in <c>DocumentMetadata.MeetingRule</c> too.</summary>
    public const string MeetingRuleFieldKey = "meetingRule";

    /// <summary>M75 (d): the printed date, which the answer above can fill in when it is blank.</summary>
    public const string MeetingDateFieldKey = "meetingDateText";

    /// <summary>The lowest year the wizard will accept — earlier than any lodge this app serves.</summary>
    public const int EarliestYear = 1900;

    /// <summary>The highest. A typo of three or five digits is the failure being caught, not the year 2200.</summary>
    public const int LatestYear = 2200;

    /// <summary>The empty first row of the month list — see <see cref="MonthChoices"/>.</summary>
    private const string NoMonthChosenLabel = "Please choose the month";

    /// <summary>
    /// The twelve months, spelled out, with an empty row in front (M75 (a)).
    ///
    /// <para><b>Not a date picker.</b> A calendar control asks for a day the issue has not got,
    /// arrives pre-set to today — the assumption the owner's first ruling forbids — and is the
    /// hardest control in the toolkit to drive from a keyboard or hear from a screen reader. A
    /// dropdown of twelve full names and a box for the year is the whole question, said the way a
    /// person would say it.</para>
    ///
    /// <para>The empty row exists because a combo box that opens on "January" while nothing has been
    /// answered is a silent lie, and silent acceptance of the model default is exactly the defect
    /// this milestone is about. It fails validation like any other unanswered question.</para>
    /// </summary>
    public static IReadOnlyList<WizardChoice> MonthChoices { get; } =
    [
        new WizardChoice("", NoMonthChosenLabel),
        .. Enumerable.Range(1, 12).Select(m => new WizardChoice(MonthName(m), MonthName(m))),
    ];

    /// <summary>"July" → 7. False for anything that is not one of the twelve spelled-out names.</summary>
    public static bool TryReadMonth(string? monthName, out int month)
    {
        for (int m = 1; m <= 12; m++)
        {
            if (string.Equals(MonthName(m), monthName?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                month = m;
                return true;
            }
        }

        month = 0;
        return false;
    }

    /// <summary>The spelled-out name of a month, invariant — the value stored in the field.</summary>
    public static string MonthName(int month) =>
        CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(month);

    public override string TypeId => "coverBanner";

    public override string DisplayName => "Cover heading";

    public override string Description =>
        "The banner at the top of the front page: lodge name, heading and this month's meeting date.";

    public override string IconKey => "coverBanner";

    public override SizePt DefaultSizePt => new(504f, 130f);

    public override IWidgetLayouter Layouter { get; } = new CoverBannerLayouter();

    protected override JsonTypeInfo<CoverBannerData> TypeInfo =>
        CoverBannerJsonContext.Default.CoverBannerData;

    public override CoverBannerData CreateEmpty(WidgetSeed seed)
    {
        ArgumentNullException.ThrowIfNull(seed);

        // LodgeName and MeetingRule come from the OPEN DOCUMENT'S OWN metadata (the seed), never a
        // shipped default — that is what makes this a scaffolding copy, not a pre-populated fake
        // person (docs/M7-spec.md §8.3). The date and both times stay blank; nobody knows them yet.
        return new CoverBannerData
        {
            LodgeName = seed.LodgeName,
            MeetingRule = seed.MeetingRule,
        };
    }

    /// <summary>
    /// M75 (a): fills in the two answers that live in the document's metadata rather than in the
    /// banner's payload, so re-editing an existing banner shows what the newsletter already knows.
    ///
    /// <para>Blank when the seed still carries the model's own defaults — January 2000, which is
    /// what a template resets to and what no real issue is. Offering "January 2000" as a pre-filled
    /// answer somebody can press past is precisely how this newsletter came to think it was
    /// January 2000 in the first place.</para>
    /// </summary>
    public override void SeedFromDocument(CoverBannerData data, WidgetSeed seed)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(seed);

        if (seed.IssueMonth is 1 && seed.IssueYear is 2000)
        {
            return;
        }

        data.IssueMonthName = seed.IssueMonth is >= 1 and <= 12 ? MonthName(seed.IssueMonth) : "";
        data.IssueYearText = seed.IssueYear.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>"Please type the year as four numbers, like 2026." — the whole of the year's rule.</summary>
    private static string? ValidateYear(string value) =>
        int.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int year)
            && year >= EarliestYear
            && year <= LatestYear
            ? null
            : "Please type the year as four numbers, like 2026.";

    protected override WizardDefinition BuildWizard() => new(
        DisplayName,
        "A few lines for the top of the front page. Leave anything blank that does not apply this month.",
        [
            // M75 (a): FIRST, and answerable in one screen. Everything else about this newsletter —
            // the file it is offered as, the month the birthday list is drawn from, the issue "what
            // we said last year" goes looking for — hangs off this one answer, and until M75 no
            // screen in the app asked it.
            new FieldsStep<CoverBannerData>(
                "Which issue is this?",
                "TrestleBoard never guesses this. It is what names the PDF you send out, and what "
                + "the birthday list is worked out from.",
                [
                    (new WizardField(
                            IssueMonthFieldKey,
                            "Month",
                            WizardFieldKind.Choice,
                            HelpText: "The month this newsletter is for.",
                            Choices: MonthChoices),
                        new WizardFieldBinding<CoverBannerData>(
                            IssueMonthFieldKey, d => d.IssueMonthName, (d, v) => d.IssueMonthName = v)),
                    (new WizardField(
                            IssueYearFieldKey,
                            "Year",
                            HelpText: "Four numbers. Any year you like — TrestleBoard does not "
                                + "assume it is this one.",
                            ExampleText: "2026",
                            MaxLength: 4,
                            Validator: ValidateYear),
                        new WizardFieldBinding<CoverBannerData>(
                            IssueYearFieldKey, d => d.IssueYearText, (d, v) => d.IssueYearText = v)),
                ]),
            new FieldsStep<CoverBannerData>(
                "What's this banner for?",
                "This prints at the very top of the newsletter, above everything else.",
                [
                    (new WizardField(
                            "lodgeName",
                            "Lodge name",
                            ExampleText: "Placeholder Lodge No. 000"),
                        new WizardFieldBinding<CoverBannerData>(
                            "lodgeName", d => d.LodgeName, (d, v) => d.LodgeName = v)),
                    (new WizardField(
                            "headingText",
                            "Heading",
                            HelpText: "This prints exactly as you type it, capital letters and all.",
                            ExampleText: "STATED COMMUNICATION"),
                        new WizardFieldBinding<CoverBannerData>(
                            "headingText", d => d.HeadingText, (d, v) => d.HeadingText = v)),
                ]),
            new FieldsStep<CoverBannerData>(
                "When is this meeting?",
                "The rule below is only used to work out next month's date automatically; the date is what actually prints.",
                [
                    (new WizardField(
                            "meetingRule",
                            "How often does the lodge meet?",
                            WizardFieldKind.DayOfMonthRule,
                            IsOptional: true,
                            ExampleText: "1st Tuesday"),
                        new WizardFieldBinding<CoverBannerData>(
                            "meetingRule", d => d.MeetingRule, (d, v) => d.MeetingRule = v)),
                    // M75 (d): optional now. It used to be the one thing a user could not get past
                    // without typing, and with the meeting rule finally reaching the newsletter the
                    // app can work it out — "1st Tuesday" plus "July 2026" is "July 7th". Left blank
                    // with no rule to compute from, the "what's next" card says so, which is what
                    // CoverDateMissing has been for since M11.
                    (new WizardField(
                            "meetingDateText",
                            "This month's meeting date",
                            IsOptional: true,
                            HelpText: "Leave it blank and TrestleBoard will work it out from the "
                                + "rule above. Type it to print something else.",
                            ExampleText: "July 7th"),
                        new WizardFieldBinding<CoverBannerData>(
                            "meetingDateText", d => d.MeetingDateText, (d, v) => d.MeetingDateText = v)),
                ]),
            new FieldsStep<CoverBannerData>(
                "Dinner and lodge times",
                "Leave either blank if it does not apply this month.",
                [
                    (new WizardField(
                            "dinnerTimeText",
                            "Dinner time",
                            WizardFieldKind.Time,
                            IsOptional: true,
                            ExampleText: "6:30"),
                        new WizardFieldBinding<CoverBannerData>(
                            "dinnerTimeText",
                            d => d.DinnerTimeText ?? "",
                            (d, v) => d.DinnerTimeText = string.IsNullOrWhiteSpace(v) ? null : v)),
                    (new WizardField(
                            "workTimeText",
                            "Lodge opens",
                            WizardFieldKind.Time,
                            IsOptional: true,
                            ExampleText: "7:30"),
                        new WizardFieldBinding<CoverBannerData>(
                            "workTimeText",
                            d => d.WorkTimeText ?? "",
                            (d, v) => d.WorkTimeText = string.IsNullOrWhiteSpace(v) ? null : v)),
                ]),
        ]);
}
