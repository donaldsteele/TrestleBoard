using Avalonia.Headless;
using Avalonia.Input;
using TrestleBoard.Core.Container;
using TrestleBoard.Editing;
using TrestleBoard.Editing.Actions;
using Xunit;

namespace TrestleBoard.App.HeadlessTests;

/// <summary>
/// M24, PLAN.md §14.1: the app can keep the user's work.
///
/// Through v0.9.1 it could not. There was no Save command of any kind — no action id, no menu item,
/// no Ctrl+S — so the only thing ever written was the crash snapshot, and the clean-close path wrote
/// that snapshot and then deleted it under the same id. A normal day's work survived only if the
/// program crashed. These tests are the standing guard on all four halves of the fix: the command
/// exists, the app knows when work is unsaved, nothing replaces a document without asking, and the
/// snapshot is deleted only once the work is somewhere else.
/// </summary>
public sealed class SaveShellTests : IDisposable
{
    private static HeadlessUnitTestSession Session => HeadlessSession.Instance;

    private readonly string _folder = Directory.CreateTempSubdirectory("tb-save-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>
    /// A recovery store that records what it was asked to do, so "was the snapshot deleted, and
    /// when?" is a question the test can answer rather than infer.
    /// </summary>
    private sealed class SpyRecoveryStore : IRecoveryStore
    {
        private readonly Dictionary<string, RecoverySnapshot> _held = new(StringComparer.Ordinal);

        internal int Writes { get; private set; }

        internal int Deletes { get; private set; }

        internal bool HoldsAnything => _held.Count > 0;

        public void Write(RecoverySnapshot snapshot)
        {
            _held[snapshot.Id] = snapshot;
            Writes++;
        }

        public void Delete(string id)
        {
            _held.Remove(id);
            Deletes++;
        }

        public IReadOnlyList<RecoverySnapshot> FindRecoverable() => _held.Values.ToList();
    }

    /// <summary>Types into the cover body frame — the smallest edit that is a real one.</summary>
    private static void MakeAnEdit(MainWindow window)
    {
        Assert.True(window.EditorForTest!.TryBeginAt(0, 100f, 200f));
        window.EditorForTest.InsertText("Brethren, ");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public async Task ControlSReachesTheSaveCommand()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.Show();
            window.OpenIssueSample();

            var reached = new List<string>();
            window.ActionsForTest.InterceptorForTest = id =>
            {
                reached.Add(id);
                return true;
            };

            window.KeyPressQwerty(PhysicalKey.S, RawInputModifiers.Control);
            window.KeyPressQwerty(PhysicalKey.S, RawInputModifiers.Control | RawInputModifiers.Shift);

            Assert.Equal([ActionId.Save, ActionId.SaveAs], reached);

            window.ActionsForTest.InterceptorForTest = null;
            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// The title bar is the only place a user can see this at a glance, and it says it in words
    /// rather than with an asterisk nobody explained to them.
    /// </summary>
    [Fact]
    public async Task EditingMarksTheNewsletterUnsavedAndSavingClearsIt()
    {
        string path = Path.Combine(_folder, "newsletter.tboard");

        await Session.Dispatch(async () =>
        {
            var window = new MainWindow();
            window.Show();
            window.OpenIssueSample();

            Assert.False(window.HasUnsavedChangesForTest);
            Assert.DoesNotContain("not saved", window.Title!, StringComparison.Ordinal);

            MakeAnEdit(window);

            Assert.True(window.HasUnsavedChangesForTest);
            Assert.Contains("not saved yet", window.Title!, StringComparison.Ordinal);
            Assert.True(ActionCatalog.Evaluate(ActionId.Save, window.CurrentActionContext).IsAvailable);

            Assert.True(await window.SaveToPathForTest(path));

            Assert.False(window.HasUnsavedChangesForTest);
            Assert.DoesNotContain("not saved", window.Title!, StringComparison.Ordinal);
            Assert.Equal(path, window.DocumentPathForTest);
            Assert.Contains("Saved to newsletter.tboard", window.StatusLabelTextForTest!, StringComparison.Ordinal);

            // Saving an unchanged newsletter is refused, and the refusal is the sentence that
            // answers what the user was really asking: is my work safe?
            ActionAvailability again = ActionCatalog.Evaluate(ActionId.Save, window.CurrentActionContext);
            Assert.False(again.IsAvailable);
            Assert.Contains("newsletter.tboard", again.Reason, StringComparison.Ordinal);

            window.Close();
        }, TestContext.Current.CancellationToken);

        // The point of all of it: the work is a file on disk that opens again.
        Assert.True(File.Exists(path));
        using var buffer = new MemoryStream(File.ReadAllBytes(path));
        TboardPackage reopened = TboardContainer.Load(buffer);
        Assert.NotEmpty(reopened.Document.Pages);
    }

    /// <summary>
    /// The regression test for the bug at the centre of §14.1. Closing cleanly deletes the crash
    /// snapshot — that is what makes a surviving snapshot mean "the app did not close cleanly". What
    /// was wrong was the order: the close path wrote a snapshot and then deleted it, so the delete
    /// was the last word on work that had never reached the user's file. Now the snapshot is deleted
    /// only after the work is saved, and a close with unsaved work asks first.
    /// </summary>
    [Fact]
    public async Task TheCrashSnapshotIsOnlyDroppedOnceTheWorkIsInTheUsersFile()
    {
        string path = Path.Combine(_folder, "kept.tboard");
        var store = new SpyRecoveryStore();

        await Session.Dispatch(async () =>
        {
            var window = new MainWindow();
            window.UseRecoveryStoreForTest(store);
            window.Show();
            window.OpenIssueSample();

            MakeAnEdit(window);
            Assert.True(window.RecoveryForTest!.SaveNow());
            Assert.True(store.HoldsAnything);

            int deletesBeforeSaving = store.Deletes;

            Assert.True(await window.SaveToPathForTest(path));

            // Saving is what makes the snapshot redundant, so that is when it goes.
            Assert.True(store.Deletes > deletesBeforeSaving);
            Assert.False(store.HoldsAnything);

            window.Close();
        }, TestContext.Current.CancellationToken);

        Assert.True(File.Exists(path));
    }

    /// <summary>
    /// Every path that replaces the open newsletter asks first, and "Go back" means the newsletter
    /// on screen is still there afterwards. Before M24 all of these replaced it without a word.
    /// </summary>
    [Fact]
    public async Task NothingReplacesUnsavedWorkWithoutAsking()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow();
            window.Show();
            window.OpenIssueSample();
            MakeAnEdit(window);

            string editedTitle = window.SessionForTest!.Document.Metadata.Title;
            int pagesBefore = window.SessionForTest.Document.Pages.Count;

            window.SaveFirstAnswerForTest = MainWindow.SaveFirst.Stay;

            await window.OpenNewsletterAsync();
            await window.NewFromTemplateAsync();
            await window.StartFromLastMonthAsync();
            await window.OpenDocumentFromPathAsync(Path.Combine(_folder, "does-not-matter.tboard"));

            // Still the same newsletter, still unsaved, nothing quietly thrown away.
            Assert.True(window.HasUnsavedChangesForTest);
            Assert.Equal(editedTitle, window.SessionForTest!.Document.Metadata.Title);
            Assert.Equal(pagesBefore, window.SessionForTest.Document.Pages.Count);

            // And the window itself refuses to go while the answer is "Go back".
            window.Close();
            Assert.True(window.IsVisible);

            window.SaveFirstAnswerForTest = MainWindow.SaveFirst.Discard;
            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// A carried-forward issue exists in no file anywhere, and work put back by the recovery dialog
    /// is unsaved by definition — that is why it was in the recovery store. Both say so from the
    /// first moment rather than looking like a document that is already safe.
    /// </summary>
    [Fact]
    public async Task CarryForwardStartsUnsaved()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.Show();
            window.OpenIssueSample();
            Assert.False(window.HasUnsavedChangesForTest);

            Assert.True(window.StartFromLastMonth());
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            Assert.True(window.HasUnsavedChangesForTest);
            Assert.Null(window.DocumentPathForTest);
            Assert.Contains("not saved yet", window.Title!, StringComparison.Ordinal);
            Assert.True(ActionCatalog.Evaluate(ActionId.Save, window.CurrentActionContext).IsAvailable);

            window.SaveFirstAnswerForTest = MainWindow.SaveFirst.Discard;
            window.Close();
        }, TestContext.Current.CancellationToken);
    }
}
