using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TrestleBoard.App.Dialogs;
using TrestleBoard.App.Integration;
using TrestleBoard.App.Settings;
using TrestleBoard.Core.Container;
using TrestleBoard.Editing.Actions;
using Xunit;

namespace TrestleBoard.App.HeadlessTests;

/// <summary>
/// Packing a committee's whole working life up and putting it back (PLAN.md §11 M64).
///
/// <para>The container itself is held by <c>Core.Tests/SuccessorPackTests</c>. What is left for here
/// is the half that is about a real computer: that every store is found, that a clean machine ends
/// up byte-for-byte identical, that taking one thing touches only that thing, and that a file which
/// arrived by email cannot write outside the app's own folder.</para>
///
/// <para>Every fixture is fictional (§0 rule 2/5). These tests write into a temporary app-state root
/// of their own — they never touch <c>%AppData%/TrestleBoard</c>, which on a maintainer's machine
/// holds the real lodge address book.</para>
/// </summary>
public sealed class SuccessorPackTests
{
    // ---- gathering --------------------------------------------------------------------------------

    [Fact]
    public void EveryStoreOnThisComputerGoesIntoThePack()
    {
        using var here = new TemporaryComputer();
        here.SeedEverything();

        SuccessorPackage pack = SuccessorPackService.Gather(WhenItWasPacked, "1.2.3");

        Assert.Equal(
            [
                SuccessorPackParts.Roster,
                SuccessorPackParts.Templates,
                SuccessorPackParts.Phrases,
                SuccessorPackParts.Dictionary,
                SuccessorPackParts.Settings,
            ],
            pack.Manifest.Parts.Select(p => p.Id));

        Assert.Equal(
            [
                "roster/roster-backups/roster-20260101-000000-000.roster.bak.json",
                "roster/roster.json",
            ],
            pack.FilesOf(SuccessorPackParts.Roster).Select(f => f.Key));

        // Both files of the template: the .tboard and the sidecar that holds the name the user gave
        // it. Taking one without the other hands the successor a shelf of layouts called "example".
        Assert.Equal(
            ["templates/example.json", "templates/example.tboard"],
            pack.FilesOf(SuccessorPackParts.Templates).Select(f => f.Key));
    }

    /// <summary>A computer with nothing on it packs nothing, rather than an empty file full of parts.</summary>
    [Fact]
    public void AComputerWithNothingOnItHasNothingToPack()
    {
        using var here = new TemporaryComputer();

        SuccessorPackage pack = SuccessorPackService.Gather(WhenItWasPacked, "1.2.3");

        Assert.Empty(pack.Manifest.Parts);
        Assert.Empty(pack.Files);
    }

    /// <summary>
    /// A store that cannot be read costs the pack that store, not the whole pack. Four of five
    /// things is a far better afternoon for the successor than an error message.
    /// </summary>
    [Fact]
    public void AMissingStoreIsLeftOutRatherThanFailingTheWholePack()
    {
        using var here = new TemporaryComputer();
        here.SeedEverything();
        File.Delete(AppPaths.PhraseShelfFile);

        SuccessorPackage pack = SuccessorPackService.Gather(WhenItWasPacked, "1.2.3");

        Assert.Null(pack.PartOf(SuccessorPackParts.Phrases));
        Assert.NotNull(pack.PartOf(SuccessorPackParts.Roster));
    }

    /// <summary>
    /// §0 rule 7: the summary is written into the manifest, which is the part of the pack anybody
    /// poking at it will read first. It counts and it never names.
    /// </summary>
    [Fact]
    public void TheManifestCountsAndNeverNames()
    {
        using var here = new TemporaryComputer();
        here.SeedEverything();

        SuccessorPackage pack = SuccessorPackService.Gather(WhenItWasPacked, "1.2.3");

        foreach (SuccessorPackPart part in pack.Manifest.Parts)
        {
            Assert.DoesNotContain("Placeholder", part.Summary, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("@", part.Summary, StringComparison.Ordinal);
        }

        Assert.Equal("2 people", pack.PartOf(SuccessorPackParts.Roster)?.Summary);
        Assert.Equal("1 template", pack.PartOf(SuccessorPackParts.Templates)?.Summary);
    }

    // ---- the acceptance ---------------------------------------------------------------------------

    /// <summary>
    /// PLAN.md's acceptance, word for word: <i>round trip on a clean app-state root restores every
    /// store byte-for-byte</i>.
    /// </summary>
    [Fact]
    public void ACleanComputerEndsUpWithEveryStoreByteForByte()
    {
        Dictionary<string, byte[]> before;
        SuccessorPackage pack;
        using (var old = new TemporaryComputer())
        {
            old.SeedEverything();
            before = old.EveryFile();
            pack = SuccessorPackService.Gather(WhenItWasPacked, "1.2.3");
        }

        using var fresh = new TemporaryComputer();
        RestoreOutcome outcome = SuccessorPackService.Restore(pack, [.. SuccessorPackParts.InOrder]);

        Assert.Empty(outcome.Failed);
        Assert.Equal(SuccessorPackParts.InOrder, outcome.Restored);

        Dictionary<string, byte[]> after = fresh.EveryFile();
        Assert.Equal(before.Keys.Order(StringComparer.Ordinal), after.Keys.Order(StringComparer.Ordinal));
        foreach ((string relative, byte[] bytes) in before)
        {
            Assert.Equal(bytes, after[relative]);
        }
    }

    /// <summary>
    /// The other half of the acceptance: <i>a partial restore (roster only, say) touches nothing
    /// else</i>. The receiving committee's own templates and wordings are theirs.
    /// </summary>
    [Fact]
    public void TakingOnlyTheAddressBookTouchesNothingElse()
    {
        SuccessorPackage pack;
        using (var old = new TemporaryComputer())
        {
            old.SeedEverything();
            pack = SuccessorPackService.Gather(WhenItWasPacked, "1.2.3");
        }

        using var mine = new TemporaryComputer();
        TemporaryComputer.Write(AppPaths.PhraseShelfFile, "[]");
        TemporaryComputer.Write(AppPaths.PersonalDictionaryFile, "Ourword\n");

        SuccessorPackService.Restore(pack, [SuccessorPackParts.Roster]);

        Assert.True(File.Exists(AppPaths.RosterFile));
        Assert.Equal("[]", File.ReadAllText(AppPaths.PhraseShelfFile));
        Assert.Equal("Ourword\n", File.ReadAllText(AppPaths.PersonalDictionaryFile).Replace("\r\n", "\n"));
        Assert.False(Directory.Exists(AppPaths.TemplatesDirectory));
    }

    /// <summary>
    /// The one rule: the pack's files are written over the same paths, and nothing the pack does not
    /// carry is deleted. A restore that also tidied up would quietly destroy the receiving
    /// committee's own work while calling itself a copy.
    /// </summary>
    [Fact]
    public void ATemplateOfMyOwnSurvivesSomebodyElsesPack()
    {
        SuccessorPackage pack;
        using (var old = new TemporaryComputer())
        {
            old.SeedEverything();
            pack = SuccessorPackService.Gather(WhenItWasPacked, "1.2.3");
        }

        using var mine = new TemporaryComputer();
        string ours = Path.Combine(AppPaths.TemplatesDirectory, "ours.tboard");
        TemporaryComputer.Write(ours, "ours");

        SuccessorPackService.Restore(pack, [SuccessorPackParts.Templates]);

        Assert.Equal("ours", File.ReadAllText(ours));
        Assert.True(File.Exists(Path.Combine(AppPaths.TemplatesDirectory, "example.tboard")));
    }

    /// <summary>
    /// Restoring a predecessor's address book over one that is already here is almost certainly what
    /// the user meant — but "almost" is not good enough for the only copy of the lodge's membership,
    /// so the ring gets a copy of what was here first.
    /// </summary>
    [Fact]
    public void TheAddressBookThatIsHereIsKeptBeforeItIsReplaced()
    {
        SuccessorPackage pack;
        using (var old = new TemporaryComputer())
        {
            old.SeedEverything();
            pack = SuccessorPackService.Gather(WhenItWasPacked, "1.2.3");
        }

        using var mine = new TemporaryComputer();
        TemporaryComputer.Write(AppPaths.RosterFile, MineRosterJson);

        SuccessorPackService.Restore(pack, [SuccessorPackParts.Roster]);

        string ring = Path.Combine(AppPaths.Root, "roster-backups");
        string[] kept = Directory.GetFiles(ring, "*.roster.bak.json");
        Assert.Contains(kept, p => File.ReadAllText(p) == MineRosterJson);
        Assert.NotEqual(MineRosterJson, File.ReadAllText(AppPaths.RosterFile));
    }

    /// <summary>
    /// Every restored file is written to a temp name and renamed over, so a disk that fills up half
    /// way through cannot replace a good address book with a truncated one. Nothing may be left
    /// behind by the successful path — a littered <c>roster.json.tmp</c> is what M24 found in the
    /// document saver and fixed there.
    /// </summary>
    [Fact]
    public void RestoringLeavesNoHalfWrittenFilesBehind()
    {
        SuccessorPackage pack;
        using (var old = new TemporaryComputer())
        {
            old.SeedEverything();
            pack = SuccessorPackService.Gather(WhenItWasPacked, "1.2.3");
        }

        using var fresh = new TemporaryComputer();
        SuccessorPackService.Restore(pack, [.. SuccessorPackParts.InOrder]);

        Assert.DoesNotContain(fresh.EveryFile().Keys, k => k.EndsWith(".tmp", StringComparison.Ordinal));
    }

    // ---- a file that arrived by email --------------------------------------------------------------

    /// <summary>
    /// A pack is a zip somebody else made, and a zip entry name is a string somebody else chose.
    /// Every one of these is a valid entry name and none of them may become a path this app writes.
    /// </summary>
    [Theory]
    [InlineData("templates/../../../evil.txt")]
    [InlineData("templates/..\\..\\evil.txt")]
    [InlineData("templates/sub/one.tboard")]
    [InlineData("templates/..")]
    [InlineData("templates/")]
    [InlineData("roster/roster-backups/../../roster.json")]
    [InlineData("roster/other.json")]
    [InlineData("settings/somethingelse.json")]
    [InlineData("stationery/letterhead.svg")]
    [InlineData("")]
    public void AnEntryThatIsNotOneOfOursGoesNowhere(string entry)
    {
        using var here = new TemporaryComputer();

        Assert.Null(SuccessorPackService.LocalPathFor(entry));
    }

    [Fact]
    public void TheEntriesWeDoKnowLandExactlyWhereTheyShould()
    {
        using var here = new TemporaryComputer();

        Assert.Equal(AppPaths.RosterFile, SuccessorPackService.LocalPathFor("roster/roster.json"));
        Assert.Equal(AppPaths.PhraseShelfFile, SuccessorPackService.LocalPathFor("phrases/phrases.json"));
        Assert.Equal(
            AppPaths.PersonalDictionaryFile,
            SuccessorPackService.LocalPathFor("dictionary/personal-dictionary.txt"));
        Assert.Equal(AppPaths.SettingsFile, SuccessorPackService.LocalPathFor("settings/settings.json"));
        Assert.Equal(
            Path.Combine(AppPaths.TemplatesDirectory, "one.tboard"),
            SuccessorPackService.LocalPathFor("templates/one.tboard"));
    }

    /// <summary>An entry with nowhere to go is skipped, and the rest of its part still lands.</summary>
    [Fact]
    public void AHostileEntryDoesNotStopTheHonestOnes()
    {
        var pack = new SuccessorPackage();
        pack.Files["templates/../../../evil.txt"] = Utf8("no");
        pack.Files["templates/example.tboard"] = Utf8("yes");
        pack.Manifest.Parts.Add(new SuccessorPackPart(SuccessorPackParts.Templates, 2, "1 template"));

        using var here = new TemporaryComputer();
        SuccessorPackService.Restore(pack, [SuccessorPackParts.Templates]);

        Assert.Equal("yes", File.ReadAllText(Path.Combine(AppPaths.TemplatesDirectory, "example.tboard")));
        Assert.False(File.Exists(Path.Combine(AppPaths.Root, "..", "..", "..", "evil.txt")));
    }

    // ---- what the person is asked ------------------------------------------------------------------

    [Fact]
    public void OnlyWhatIsInThePackIsOfferedAndItComesInAskingOrder()
    {
        SuccessorPackage pack;
        using (var old = new TemporaryComputer())
        {
            TemporaryComputer.Write(AppPaths.RosterFile, TheirRosterJson);
            TemporaryComputer.Write(AppPaths.SettingsFile, "{}");
            pack = SuccessorPackService.Gather(WhenItWasPacked, "1.2.3");
        }

        using var fresh = new TemporaryComputer();
        IReadOnlyList<PackPartChoice> choices = SuccessorPackService.Choices(pack);

        Assert.Equal([SuccessorPackParts.Roster, SuccessorPackParts.Settings], choices.Select(c => c.Id));
        Assert.All(choices, c => Assert.False(c.WouldReplace));
        Assert.All(choices, c => Assert.False(string.IsNullOrWhiteSpace(c.Incoming)));
    }

    [Fact]
    public void SomethingAlreadyHereIsSaidToBeAtStake()
    {
        SuccessorPackage pack;
        using (var old = new TemporaryComputer())
        {
            old.SeedEverything();
            pack = SuccessorPackService.Gather(WhenItWasPacked, "1.2.3");
        }

        using var mine = new TemporaryComputer();
        TemporaryComputer.Write(AppPaths.RosterFile, MineRosterJson);

        PackPartChoice roster = SuccessorPackService.Choices(pack).Single(c => c.Id == SuccessorPackParts.Roster);

        Assert.True(roster.WouldReplace);
        Assert.Equal("1 person", roster.AlreadyHere);
        Assert.Equal("2 people", roster.Incoming);
    }

    /// <summary>
    /// The window's central promise: taking is one click, replacing is one click and a decision.
    /// A successor meeting this on their first day must not be able to lose the address book by
    /// pressing the default button.
    /// </summary>
    [Fact]
    public async Task NothingThatWouldReplaceSomethingIsTickedToStartWith()
    {
        await HeadlessSession.Instance.Dispatch(() =>
        {
            var window = new BringInPackWindow(
                [
                    Choice(SuccessorPackParts.Roster, incoming: "84 people", alreadyHere: "12 people"),
                    Choice(SuccessorPackParts.Phrases, incoming: "6 saved wordings", alreadyHere: ""),
                ],
                WhenItWasPacked,
                "TrestleBoard.tbpack");

            Assert.False(window.RowsForTest[0].Box.IsChecked);
            Assert.True(window.RowsForTest[1].Box.IsChecked);

            window.TakeForTest();
            Assert.Equal([SuccessorPackParts.Phrases], window.Chosen);
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task TheWindowSaysHowMuchOfWhatIsHereWouldGo()
    {
        await HeadlessSession.Instance.Dispatch(() =>
        {
            var window = new BringInPackWindow(
                [
                    Choice(SuccessorPackParts.Roster, "84 people", "12 people"),
                    Choice(SuccessorPackParts.Phrases, "6 saved wordings", ""),
                ],
                WhenItWasPacked,
                "TrestleBoard.tbpack");

            Assert.Contains("Nothing here will be replaced", window.SummaryForTest, StringComparison.Ordinal);

            window.TickForTest(SuccessorPackParts.Roster, true);
            Assert.Contains("replaces something", window.SummaryForTest, StringComparison.Ordinal);

            window.TickForTest(SuccessorPackParts.Roster, false);
            window.TickForTest(SuccessorPackParts.Phrases, false);
            Assert.Contains("nothing on this computer will change", window.SummaryForTest, StringComparison.Ordinal);
        }, TestContext.Current.CancellationToken);
    }

    // ---- through the real window ---------------------------------------------------------------------

    /// <summary>
    /// The whole milestone in one test, driven through the shell: an old computer packs up, a new
    /// one brings it in, and the address book is there afterwards. The two flows are wired to the
    /// catalog and the menu bar by <c>MenuIndexTests</c>; this is the part those cannot see.
    /// </summary>
    [Fact]
    public async Task OneComputerPacksUpAndAnotherBringsItIn()
    {
        string file = Path.Combine(
            Path.GetTempPath(), "TrestleBoard-pack-tests", Guid.NewGuid().ToString("N") + ".tbpack");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);

        try
        {
            await HeadlessSession.DispatchAsync(
                async () =>
                {
                    using (var old = new TemporaryComputer())
                    {
                        old.SeedEverything();
                        var leaving = new MainWindow { PackConfirmForTest = true, PackPathForTest = file };
                        await leaving.PackUpForSuccessorAsync();
                        leaving.Close();
                    }

                    Assert.True(File.Exists(file), "the pack was not written");

                    using var fresh = new TemporaryComputer();
                    var arriving = new MainWindow
                    {
                        BringInPackPathForTest = file,
                        PackPartsAnswerForTest = [SuccessorPackParts.Roster, SuccessorPackParts.Phrases],
                    };

                    await arriving.BringInAPackAsync();

                    Assert.Equal(
                        [SuccessorPackParts.Roster, SuccessorPackParts.Phrases],
                        arriving.LastRestoreForTest?.Restored);
                    Assert.True(File.Exists(AppPaths.RosterFile));
                    Assert.True(File.Exists(AppPaths.PhraseShelfFile));

                    // Not asked for, so not written — the partial-restore promise, through the shell.
                    Assert.False(File.Exists(AppPaths.PersonalDictionaryFile));

                    // The running app picked up what was written underneath it. A successor's first
                    // move after restoring is to open the address book and check it worked.
                    Assert.Equal(2, arriving.Roster.Book.Members.Count);

                    arriving.Close();
                },
                TestContext.Current.CancellationToken);
        }
        finally
        {
            File.Delete(file);
        }
    }

    /// <summary>Taking nothing is a real answer, and it must leave the computer alone.</summary>
    [Fact]
    public async Task TakingNothingChangesNothing()
    {
        string file = Path.Combine(
            Path.GetTempPath(), "TrestleBoard-pack-tests", Guid.NewGuid().ToString("N") + ".tbpack");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);

        try
        {
            await HeadlessSession.DispatchAsync(
                async () =>
                {
                    using (var old = new TemporaryComputer())
                    {
                        old.SeedEverything();
                        SuccessorPackContainer.SaveToFile(
                            SuccessorPackService.Gather(WhenItWasPacked, "1.2.3"), file);
                    }

                    using var fresh = new TemporaryComputer();
                    var window = new MainWindow
                    {
                        BringInPackPathForTest = file,
                        PackPartsAnswerForTest = [],
                    };

                    await window.BringInAPackAsync();

                    Assert.Null(window.LastRestoreForTest);
                    Assert.False(File.Exists(AppPaths.RosterFile));
                    window.Close();
                },
                TestContext.Current.CancellationToken);
        }
        finally
        {
            File.Delete(file);
        }
    }

    // ---- the catalog -------------------------------------------------------------------------------

    /// <summary>
    /// Both are reached on a computer that has never had a newsletter open — the successor's, on
    /// their first afternoon. Neither may ever be unavailable.
    /// </summary>
    [Fact]
    public void HandingOverIsNeverRefused()
    {
        var nothing = new ActionContext();

        Assert.True(ActionCatalog.Evaluate(ActionId.PackUpForSuccessor, nothing).IsAvailable);
        Assert.True(ActionCatalog.Evaluate(ActionId.BringInAPack, nothing).IsAvailable);
    }

    // ---- plumbing ----------------------------------------------------------------------------------

    private static readonly DateTimeOffset WhenItWasPacked =
        new(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Fictional throughout (§0 rule 2) — never a real member, here or anywhere.</summary>
    private const string TheirRosterJson =
        """{"schemaVersion":1,"members":[{"id":"m1","displayName":"A. Placeholder"},{"id":"m2","displayName":"B. Placeholder"}]}""";

    private const string MineRosterJson =
        """{"schemaVersion":1,"members":[{"id":"m9","displayName":"C. Placeholder"}]}""";

    private static PackPartChoice Choice(string id, string incoming, string alreadyHere) =>
        new(id,
            SuccessorPackParts.TitleOf(id),
            SuccessorPackParts.DescriptionOf(id),
            incoming,
            alreadyHere,
            alreadyHere.Length > 0);

    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    /// <summary>
    /// An app-state root of this test's own. Not tidiness: the real one holds the lodge's address
    /// book (§0 rule 5), and a test that could read it could print it into a public CI log.
    /// </summary>
    private sealed class TemporaryComputer : IDisposable
    {
        private readonly string _previous = AppPaths.Root;
        private readonly string _root;

        internal TemporaryComputer()
        {
            _root = Path.Combine(Path.GetTempPath(), "TrestleBoard-pack-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            AppPaths.Root = _root;
        }

        internal void SeedEverything()
        {
            Write(AppPaths.RosterFile, TheirRosterJson);
            Write(
                Path.Combine(_root, "roster-backups", "roster-20260101-000000-000.roster.bak.json"),
                TheirRosterJson);
            Write(Path.Combine(AppPaths.TemplatesDirectory, "example.tboard"), "PK pretend");
            Write(
                Path.Combine(AppPaths.TemplatesDirectory, "example.json"),
                """{"name":"Stated Communication","savedAt":"2026-07-01T00:00:00+00:00"}""");
            Write(AppPaths.PhraseShelfFile, """[{"id":"p1","title":"A memorial","text":"With regret."}]""");
            Write(AppPaths.PersonalDictionaryFile, "Placeholder\nTrestleboard\n");
            Write(AppPaths.SettingsFile, """{"uiScalePercent":125}""");
        }

        internal static void Write(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }

        /// <summary>Every file under the root, keyed by its path relative to it.</summary>
        internal Dictionary<string, byte[]> EveryFile() =>
            Directory.GetFiles(_root, "*", SearchOption.AllDirectories)
                .ToDictionary(
                    p => Path.GetRelativePath(_root, p).Replace('\\', '/'),
                    File.ReadAllBytes,
                    StringComparer.Ordinal);

        public void Dispose()
        {
            AppPaths.Root = _previous;
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // A temporary folder that will not go is the operating system's business, not a
                // reason to fail a test about packing.
            }
        }
    }
}
