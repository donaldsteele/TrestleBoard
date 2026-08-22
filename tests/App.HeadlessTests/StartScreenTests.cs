using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using TrestleBoard.App.Dialogs;
using TrestleBoard.App.Integration;
using TrestleBoard.Core.Container;
using TrestleBoard.Core.Templates;
using Xunit;

namespace TrestleBoard.App.HeadlessTests;

/// <summary>
/// M76 (h), spec §7: the two things the start screen's own screenshot showed.
///
/// <para>The template picker sat below all three tiles, outside the offer it modifies; and the
/// committee that reopens the same newsletter every evening for a week had to walk a file dialog
/// each time. Both are §6 costs, and neither is a repaint.</para>
///
/// <para>§0 rule 2: every fixture here is fictional — placeholder file names in a temporary
/// folder, and the only newsletter any of these opens is a stock template this suite wrote
/// itself.</para>
/// </summary>
public sealed class StartScreenTests : IDisposable
{
    private static HeadlessUnitTestSession Session => HeadlessSession.Instance;

    private readonly string _folder = Path.Combine(
        Path.GetTempPath(), "TrestleBoard-recent-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_folder))
            {
                Directory.Delete(_folder, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    // ---- the recent list on the screen ----------------------------------------------------------

    /// <summary>
    /// The shortcut exists, is a labelled button rather than an icon or a bare path, is big enough
    /// to hit, and says something that tells one newsletter from another.
    /// </summary>
    [Fact]
    public async Task RecentNewslettersAreOfferedAsFullWidthLabelledButtons()
    {
        await Session.Dispatch(() =>
        {
            var dialog = new StartDialog(
                canStartFromLastMonth: true,
                [],
                [
                    new RecentIssue(@"C:\newsletters\Trestle Board 2026-07.tboard",
                        "Trestle Board 2026-07", "Last saved on 14 July 2026"),
                    new RecentIssue(@"C:\newsletters\Trestle Board 2026-06.tboard",
                        "Trestle Board 2026-06", "Last saved on 9 June 2026"),
                ]);
            dialog.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            StackPanel? list = dialog.RecentListForTest;
            Assert.NotNull(list);

            List<Button> buttons = list!.Children.OfType<Button>().ToList();
            Assert.Equal(2, buttons.Count);

            foreach (Button button in buttons)
            {
                // §6: 44px minimum, a real name for a screen reader, and never greyed.
                Assert.True(button.MinHeight >= 44, $"{AutomationProperties.GetName(button)} is too short");
                Assert.True(button.IsEnabled);
                Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(button)));
                Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetHelpText(button)));
                Assert.Contains("action", button.Classes);
            }

            // Newest first, and the second line is what tells two issues apart.
            Assert.Equal("Trestle Board 2026-07", AutomationProperties.GetName(buttons[0]));
            Assert.Equal("Last saved on 14 July 2026", AutomationProperties.GetHelpText(buttons[0]));

            dialog.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Pressing one answers with a path and nothing else — the shell opens it by the ordinary open
    /// path, so nothing in this window loads a newsletter.
    /// </summary>
    [Fact]
    public async Task PressingARecentNewsletterAnswersWithItsPath()
    {
        await Session.Dispatch(() =>
        {
            var dialog = new StartDialog(
                canStartFromLastMonth: true,
                [],
                [
                    new RecentIssue(@"C:\newsletters\Trestle Board 2026-07.tboard",
                        "Trestle Board 2026-07", "Last saved on 14 July 2026"),
                ]);
            dialog.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            Button button = dialog.RecentListForTest!.Children.OfType<Button>().Single();
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            Assert.Equal(StartChoice.RecentFile, dialog.Choice);
            Assert.Equal(@"C:\newsletters\Trestle Board 2026-07.tboard", dialog.SelectedRecentPath);
            Assert.False(dialog.IsVisible);
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// <b>And the shell actually opens it.</b>
    ///
    /// <para>The test above asks the dialog what it decided, which is all it can see — and that is
    /// exactly how M76 (h) shipped half-wired: the start screen gained the choice and neither
    /// switch in the shell that consumes a start-screen answer had an arm for it, so pressing a
    /// recent newsletter closed the window and opened nothing, in silence, with the suite green
    /// over it. So this one drives the shell instead: press the tile, hand the answer to the window
    /// the way the two real callers do, and ask the WINDOW whether a newsletter came up.</para>
    ///
    /// <para>§0 rule 2: the newsletter is a stock template written to a temporary folder under a
    /// fictional name. Nothing real is opened here.</para>
    /// </summary>
    [Fact]
    public async Task PressingARecentNewsletterOpensItInTheShell()
    {
        Directory.CreateDirectory(_folder);
        string path = Path.Combine(_folder, "Trestle Board 2026-07.tboard");
        TboardContainer.SaveToFile(TemplateLibrary.Create(TemplateLibrary.All[0].Id), path);

        await HeadlessSession.DispatchAsync(
            async () =>
            {
                var window = new MainWindow();
                window.Show();
                window.SaveFirstAnswerForTest = MainWindow.SaveFirst.Discard;
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.Null(window.PackageForTest);

                var dialog = new StartDialog(
                    canStartFromLastMonth: false,
                    [],
                    [new RecentIssue(path, "Trestle Board 2026-07", "Last saved on 14 July 2026")]);
                dialog.Show();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                Button button = dialog.RecentListForTest!.Children.OfType<Button>().Single();
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                bool opened = await window.ActOnWhatTheStartScreenSaidAsync(dialog);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                Assert.True(opened, "the start screen's recent newsletter did not come up");
                Assert.NotNull(window.PackageForTest);
                Assert.Equal(path, window.DocumentPathForTest);

                window.Close();
            },
            TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// First run: the section is ABSENT, not an empty box and not a greyed one. A brand-new
    /// installation must not open on a shape that says something is missing.
    /// </summary>
    [Fact]
    public async Task WithNothingToOfferTheRecentSectionIsNotBuiltAtAll()
    {
        await Session.Dispatch(() =>
        {
            var dialog = new StartDialog(canStartFromLastMonth: true, [], []);
            dialog.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            Assert.Null(dialog.RecentListForTest);

            // Not merely hidden: the heading is not in the tree either.
            Assert.DoesNotContain(
                dialog.GetLogicalDescendants().OfType<TextBlock>(),
                t => (t.Text ?? string.Empty).Contains("recently", StringComparison.OrdinalIgnoreCase));

            dialog.Close();
        }, TestContext.Current.CancellationToken);
    }

    // ---- the picker that was orphaned -----------------------------------------------------------

    /// <summary>
    /// The choice lives with the offer it modifies: "Which template" and the "Start from a
    /// template" tile share one container, and the combo is still reachable and still named.
    /// </summary>
    [Fact]
    public async Task TheTemplatePickerSitsInsideTheTemplateTile()
    {
        await Session.Dispatch(() =>
        {
            var dialog = new StartDialog(canStartFromLastMonth: true, [], []);
            dialog.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            ComboBox picker = dialog.GetLogicalDescendants()
                .OfType<ComboBox>()
                .Single(c => AutomationProperties.GetName(c) == "Which template");

            Assert.True(picker.FontSize >= 16);
            Assert.True(picker.IsEnabled);

            // Walk up from the picker: some ancestor must also hold the tile it belongs to. Before
            // M76 (h) the nearest such ancestor was the whole window.
            Control? group = picker.Parent as Control;
            Button? tile = null;
            while (group is not null && tile is null)
            {
                tile = group.GetLogicalDescendants()
                    .OfType<Button>()
                    .FirstOrDefault(b => AutomationProperties.GetName(b) == "Start from a template");
                if (tile is null)
                {
                    group = group.Parent as Control;
                }
            }

            Assert.NotNull(tile);

            // And that shared container is not the window itself — the two are grouped, not merely
            // both on screen.
            Assert.NotNull(group);
            Assert.IsNotType<Window>(group);
            Assert.DoesNotContain(
                group!.GetLogicalDescendants().OfType<Button>(),
                b => AutomationProperties.GetName(b) == "Start from last month");

            dialog.Close();
        }, TestContext.Current.CancellationToken);
    }

    // ---- where the list comes from --------------------------------------------------------------

    /// <summary>
    /// The list is read from the folder M59 already asked about, newest first. No second store.
    /// </summary>
    [Fact]
    public void TheRecentListIsTheOldIssuesFolderNewestFirst()
    {
        Directory.CreateDirectory(_folder);
        string older = Write("Trestle Board 2026-06.tboard", DateTime.Now.AddDays(-30));
        string newer = Write("Trestle Board 2026-07.tboard", DateTime.Now.AddDays(-1));

        IReadOnlyList<RecentIssue> recent = PastIssues.Recent(_folder);

        Assert.Equal(new[] { newer, older }, recent.Select(r => r.Path));
        Assert.Equal("Trestle Board 2026-07", recent[0].Name);
        Assert.StartsWith("Last saved on ", recent[0].Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void ANamedFolderThatIsNotThereYieldsNothingRatherThanThrowing()
    {
        Assert.Empty(PastIssues.Recent(null));
        Assert.Empty(PastIssues.Recent("   "));
        Assert.Empty(PastIssues.Recent(Path.Combine(_folder, "no-such-folder")));
    }

    [Fact]
    public void OnlyAFewAreOffered()
    {
        Directory.CreateDirectory(_folder);
        for (int i = 1; i <= 9; i++)
        {
            Write($"Trestle Board 2026-{i:00}.tboard", DateTime.Now.AddDays(-i));
        }

        Assert.Equal(5, PastIssues.Recent(_folder).Count);
        Assert.Equal(2, PastIssues.Recent(_folder, most: 2).Count);
    }

    /// <summary>A file with the right extension and nothing inside it: this listing never opens
    /// one, so a folder of broken newsletters still comes back as a list of names.</summary>
    private string Write(string name, DateTime when)
    {
        string path = Path.Combine(_folder, name);
        File.WriteAllText(path, string.Empty);
        File.SetLastWriteTime(path, when);
        return path;
    }
}
