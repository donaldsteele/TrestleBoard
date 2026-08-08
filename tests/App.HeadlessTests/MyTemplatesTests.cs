using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TrestleBoard.App.Integration;
using TrestleBoard.App.Settings;
using TrestleBoard.Core.Container;
using TrestleBoard.Core.Model;
using TrestleBoard.Core.Samples;
using TrestleBoard.Core.Templates;
using TrestleBoard.Core.Workflow;
using Xunit;

namespace TrestleBoard.App.HeadlessTests;

/// <summary>
/// M57: keeping a layout to start from another time (PLAN.md §11 M57).
///
/// <para>§0 rule 7: a user template carries the officers table and the cover, so on a real machine
/// it holds real names. Every fixture here is the fictional sample issue, and the store is pointed
/// at a temporary folder.</para>
/// </summary>
public sealed class MyTemplatesTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(), "TrestleBoard-templates-tests", Guid.NewGuid().ToString("N"));

    private UserTemplateStore NewStore() => new(_folder);

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

    // ---- what a template keeps, and what it lets go ---------------------------------------------

    [Fact]
    public void TheLayoutIsKeptAndTheWritingIsNot()
    {
        TboardPackage issue = SampleIssue.CreatePackage();
        int pages = issue.Document.Pages.Count;
        int widgets = issue.Document.Pages.SelectMany(p => p.Blocks).OfType<WidgetBlock>().Count();

        TboardPackage template = NewsletterTemplate.From(issue);

        Assert.Equal(pages, template.Document.Pages.Count);
        Assert.Equal(widgets, template.Document.Pages.SelectMany(p => p.Blocks).OfType<WidgetBlock>().Count());
        Assert.All(template.Document.Stories, s => Assert.Equal(
            PlaceholderPrompts.Article,
            string.Concat(s.Paragraphs.SelectMany(p => p.Runs).Select(r => r.Text))));
    }

    [Fact]
    public void ThePicturesComeWithIt()
    {
        TboardPackage issue = SampleIssue.CreatePackage(TrestleBoard.App.Canvas.SamplePhoto.CreatePng());

        TboardPackage template = NewsletterTemplate.From(issue);

        Assert.Equal(issue.Assets.Count, template.Assets.Count);
        Assert.All(issue.Assets, a => Assert.Equal(a.Value, template.Assets[a.Key]));
    }

    [Fact]
    public void TheIssueDateIsCleared()
    {
        TboardPackage issue = SampleIssue.CreatePackage();
        issue.Document.Metadata.IssueMonth = 9;
        issue.Document.Metadata.IssueYear = 2026;

        TboardPackage template = NewsletterTemplate.From(issue);

        var fresh = new DocumentMetadata();
        Assert.Equal(fresh.IssueMonth, template.Document.Metadata.IssueMonth);
        Assert.Equal(fresh.IssueYear, template.Document.Metadata.IssueYear);
    }

    /// <summary>
    /// The flag has been in the manifest since M2 and, until M57, nothing ever set it — a promise
    /// the file format made and the app never kept.
    /// </summary>
    [Fact]
    public void ThePackageSaysItIsATemplate()
    {
        Assert.False(NewsletterTemplate.IsTemplate(SampleIssue.CreatePackage()));
        Assert.True(NewsletterTemplate.IsTemplate(NewsletterTemplate.From(SampleIssue.CreatePackage())));
    }

    [Fact]
    public void TheNewsletterItWasMadeFromIsUntouched()
    {
        TboardPackage issue = SampleIssue.CreatePackage();
        issue.Document.Metadata.IssueMonth = 9;
        string before = string.Concat(
            issue.Document.Stories.SelectMany(s => s.Paragraphs).SelectMany(p => p.Runs).Select(r => r.Text));

        NewsletterTemplate.From(issue);

        Assert.Equal(9, issue.Document.Metadata.IssueMonth);
        Assert.Equal(before, string.Concat(
            issue.Document.Stories.SelectMany(s => s.Paragraphs).SelectMany(p => p.Runs).Select(r => r.Text)));
        Assert.False(issue.Manifest.IsTemplate);
    }

    /// <summary>
    /// PLAN.md's acceptance: <i>the reset pipeline is shared with carry-forward, not duplicated</i>.
    /// The day they drift, a template starts carrying last month's words into every issue built
    /// from it, so this asserts they agree about the thing that matters.
    /// </summary>
    [Fact]
    public void TheResetIsTheSameOneCarryForwardUses()
    {
        TboardPackage issue = SampleIssue.CreatePackage();

        TboardPackage nextMonth = CarryForward.NextIssue(issue);
        TboardPackage template = NewsletterTemplate.From(issue);

        Assert.Equal(
            nextMonth.Document.Stories.Select(s => string.Concat(
                s.Paragraphs.SelectMany(p => p.Runs).Select(r => r.Text))),
            template.Document.Stories.Select(s => string.Concat(
                s.Paragraphs.SelectMany(p => p.Runs).Select(r => r.Text))));
    }

    // ---- the shelf ------------------------------------------------------------------------------

    [Fact]
    public void ATemplateIsThereNextTime()
    {
        UserTemplateStore store = NewStore();
        store.Save(NewsletterTemplate.From(SampleIssue.CreatePackage()), "Installation night", DateTimeOffset.Now);

        UserTemplate saved = Assert.Single(NewStore().All());

        Assert.Equal("Installation night", saved.Name);
        Assert.NotNull(NewStore().Open(saved.Id));
    }

    [Fact]
    public void SavingTwiceUnderOneNameReplacesRatherThanMakingATwin()
    {
        UserTemplateStore store = NewStore();
        store.Save(NewsletterTemplate.From(SampleIssue.CreatePackage()), "Installation night", DateTimeOffset.Now);
        store.Save(NewsletterTemplate.From(SampleIssue.CreatePackage()), "Installation night", DateTimeOffset.Now);

        Assert.Single(store.All());
    }

    /// <summary>
    /// A name with a slash or a colon in it must neither escape the folder nor refuse to save.
    /// </summary>
    [Theory]
    [InlineData("Past Masters' Night")]
    [InlineData("A/B test")]
    [InlineData("..\\..\\escape")]
    [InlineData(":::")]
    public void AnyNameTheUserTypesCanBeSaved(string name)
    {
        UserTemplate? saved = NewStore().Save(
            NewsletterTemplate.From(SampleIssue.CreatePackage()), name, DateTimeOffset.Now);

        Assert.NotNull(saved);
        Assert.Equal(name, saved.Name);
        Assert.Equal(_folder, Path.GetDirectoryName(Path.Combine(_folder, saved.Id + ".tboard")));
    }

    [Fact]
    public void RenamingKeepsTheTemplateItself()
    {
        UserTemplateStore store = NewStore();
        UserTemplate saved = store.Save(
            NewsletterTemplate.From(SampleIssue.CreatePackage()), "Old name", DateTimeOffset.Now)!;

        Assert.True(store.Rename(saved.Id, "New name", DateTimeOffset.Now));

        UserTemplate after = Assert.Single(store.All());
        Assert.Equal("New name", after.Name);
        Assert.Equal(saved.Id, after.Id);
        Assert.NotNull(store.Open(saved.Id));
    }

    [Fact]
    public void RemovingTakesBothFilesAway()
    {
        UserTemplateStore store = NewStore();
        UserTemplate saved = store.Save(
            NewsletterTemplate.From(SampleIssue.CreatePackage()), "Installation night", DateTimeOffset.Now)!;

        Assert.True(store.Remove(saved.Id));

        Assert.Empty(store.All());
        Assert.Null(store.Open(saved.Id));
        Assert.Empty(Directory.GetFiles(_folder));
    }

    /// <summary>
    /// A lost sidecar costs the template its name, not its existence. A layout somebody spent an
    /// evening on stays reachable.
    /// </summary>
    [Fact]
    public void ATemplateWhoseNameFileIsGoneIsStillThere()
    {
        UserTemplateStore store = NewStore();
        UserTemplate saved = store.Save(
            NewsletterTemplate.From(SampleIssue.CreatePackage()), "Installation night", DateTimeOffset.Now)!;
        File.Delete(Path.Combine(_folder, saved.Id + ".json"));

        UserTemplate after = Assert.Single(store.All());

        Assert.NotNull(store.Open(after.Id));
        Assert.False(string.IsNullOrWhiteSpace(after.Name));
    }

    [Fact]
    public void AFolderThatIsNotThereYetIsNotAnError() => Assert.Empty(NewStore().All());

    [Fact]
    public void ATemplateCarriesAPictureOfItsFirstPage()
    {
        TboardPackage template = NewsletterTemplate.From(SampleIssue.CreatePackage());
        template.Thumbnails["page-1.png"] = [1, 2, 3];

        UserTemplate? saved = NewStore().Save(template, "Installation night", DateTimeOffset.Now);

        Assert.Equal<byte[]?>([1, 2, 3], saved!.ThumbnailPng);
    }

    // ---- §0 rule 6: the screenshot harness's redirect has to cover this store --------------------

    /// <summary>
    /// The harness assigns <c>AppPaths.Root</c> once and every store computed off it follows. A
    /// store that hard-coded its own AppData path would bypass that and could read a real
    /// committee's templates into a screenshot.
    /// </summary>
    [Fact]
    public void TheTemplateStoreFollowsTheAppStateRoot()
    {
        Assert.StartsWith(AppPaths.Root, AppPaths.TemplatesDirectory, StringComparison.Ordinal);
        Assert.StartsWith(AppPaths.Root, new UserTemplateStore().Location, StringComparison.Ordinal);
    }
}
