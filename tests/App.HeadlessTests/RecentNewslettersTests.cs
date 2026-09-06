using System;
using System.IO;
using TrestleBoard.App.Dialogs;
using TrestleBoard.App.Startup;
using TrestleBoard.Core.Container;
using TrestleBoard.Core.Templates;
using TrestleBoard.App.Settings;
using TrestleBoard.Editing.Actions;
using Xunit;

namespace TrestleBoard.App.HeadlessTests;

/// <summary>
/// M106: the newsletters you had open.
///
/// <para>M76 (h)'s list scans the old-issues folder by last-write time. It answers a different
/// question: it is empty until somebody has nominated a folder, it cannot see a newsletter kept
/// anywhere else, and a file touched by a backup tool climbs it while a newsletter opened and read
/// climbs nothing. <b>This one records opening.</b></para>
/// </summary>
public sealed class RecentNewslettersTests
{
    /// <summary>Most recent first, and a newsletter opened twice appears once.</summary>
    [Fact]
    public void TheOneOpenedLastIsAtTheFrontAndAppearsOnlyOnce()
    {
        AppSettings settings = new AppSettings()
            .WithNewsletterOpened(@"C:\lodge\July.tboard")
            .WithNewsletterOpened(@"C:\lodge\August.tboard")
            .WithNewsletterOpened(@"C:\lodge\July.tboard");

        Assert.Equal(
            [@"C:\lodge\July.tboard", @"C:\lodge\August.tboard"],
            settings.RecentNewsletters);
    }

    /// <summary>
    /// The same file spelled with different capitals is the same file. On Windows it plainly is,
    /// and a list that showed it twice would be a list somebody stopped trusting.
    /// </summary>
    [Fact]
    public void TheSameFileInDifferentCapitalsIsOneEntry()
    {
        AppSettings settings = new AppSettings()
            .WithNewsletterOpened(@"C:\lodge\July.tboard")
            .WithNewsletterOpened(@"C:\LODGE\JULY.TBOARD");

        Assert.Single(settings.RecentNewsletters);
    }

    /// <summary>It stops at eight, oldest off the end.</summary>
    [Fact]
    public void ItRemembersEightAndNoMore()
    {
        var settings = new AppSettings();
        for (int i = 0; i < 20; i++)
        {
            settings = settings.WithNewsletterOpened($@"C:\lodge\issue-{i}.tboard");
        }

        Assert.Equal(AppSettings.RecentNewslettersKept, settings.RecentNewsletters.Count);
        Assert.Equal(@"C:\lodge\issue-19.tboard", settings.RecentNewsletters[0]);
    }

    /// <summary>Nothing is remembered about nothing.</summary>
    [Fact]
    public void AnEmptyPathIsNotRemembered()
    {
        Assert.Empty(new AppSettings().WithNewsletterOpened("   ").RecentNewsletters);
        Assert.Empty(new AppSettings().WithNewsletterOpened(null).RecentNewsletters);
    }

    /// <summary>And one can be taken back off, which is what a file that has really gone gets.</summary>
    [Fact]
    public void OneThatIsReallyGoneCanBeTakenOff()
    {
        AppSettings settings = new AppSettings()
            .WithNewsletterOpened(@"C:\lodge\July.tboard")
            .WithNewsletterOpened(@"C:\lodge\August.tboard")
            .WithoutNewsletter(@"C:\LODGE\JULY.tboard");

        Assert.Equal([@"C:\lodge\August.tboard"], settings.RecentNewsletters);
    }

    /// <summary>
    /// It survives being written down and read back. A remembered list that is remembered only
    /// until the app closes is the thing this milestone exists to replace.
    /// </summary>
    [Fact]
    public void ItSurvivesBeingSavedAndLoaded()
    {
        string path = Path.Combine(Path.GetTempPath(), $"tb-m106-{Guid.NewGuid():N}.json");
        try
        {
            Assert.True(new AppSettings().WithNewsletterOpened(@"C:\lodge\July.tboard").Save(path));

            Assert.Equal([@"C:\lodge\July.tboard"], AppSettings.Load(path).RecentNewsletters);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// <b>Available with nothing open</b> — that is when it is most wanted, and it is the only
    /// command in the group whose whole purpose survives an empty window. An empty list is answered
    /// by the window, not by a refusal that tells a new user off for being new.
    /// </summary>
    [Fact]
    public void ItCanBeReachedWithNothingOpenAtAll()
    {
        Assert.True(
            ActionCatalog.Evaluate(ActionId.RecentNewsletters, new ActionContext()).IsAvailable);
    }

    /// <summary>
    /// <b>And opening one actually records it.</b> Every test above is about the rule; this is the
    /// one that says the rule is ever applied — the defect this whole audit is about is a thing
    /// that works everywhere except where it is reached from.
    /// </summary>
    [Fact]
    public async System.Threading.Tasks.Task OpeningANewsletterPutsItOnTheList()
    {
        string path = Path.Combine(Path.GetTempPath(), $"tb-m106-{Guid.NewGuid():N}.tboard");
        using (var file = File.Create(path))
        {
            TboardContainer.Save(TemplateLibrary.Create(TemplateLibrary.All[1].Id), file);
        }

        try
        {
            await HeadlessSession.Instance.Dispatch(
                () =>
                {
                    var window = new MainWindow
                    {
                        StartupOptions = new StartupOptions(path, SkipUpdateCheck: true),
                    };

                    Assert.True(window.OpenDocumentFromPath(path));
                    Assert.Contains(path, AppSettings.Load().RecentNewsletters);

                    window.Close();
                    return true;
                },
                TestContext.Current.CancellationToken);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// The folder is shown by its own name only. The whole path is the user's own filing (§0), it
    /// is usually longer than the window, and it is not what tells one issue from another.
    /// </summary>
    [Fact]
    public void TheFolderIsNamedAndTheWholePathIsNot()
    {
        string line = RecentNewslettersDialog.FolderLine(
            Path.Combine("C:", "Users", "someone", "Trestle Boards", "July.tboard"));

        Assert.Equal("In Trestle Boards", line);
        Assert.DoesNotContain("someone", line, StringComparison.Ordinal);
    }
}
