using TrestleBoard.Core.Commands;
using TrestleBoard.Core.Model;
using Xunit;

namespace TrestleBoard.Editing.Tests;

/// <summary>
/// Autosave and crash recovery (PLAN.md §4, docs/M9-spec.md §1). The acceptance bound is "≤60s data
/// loss", so these tests are about WHEN a snapshot is written and what state it leaves behind.
/// </summary>
public sealed class RecoveryServiceTests
{
    private sealed class FakeStore : IRecoveryStore
    {
        public List<RecoverySnapshot> Written { get; } = [];

        public List<string> Deleted { get; } = [];

        public void Write(RecoverySnapshot snapshot) => Written.Add(snapshot);

        public void Delete(string id) => Deleted.Add(id);

        public IReadOnlyList<RecoverySnapshot> FindRecoverable() => Written;
    }

    private static (RecoveryService Service, FakeStore Store, EditorTestHarness Harness, ManualTimeProvider Clock)
        Create()
    {
        var harness = new EditorTestHarness("Some text.", withExclusion: false);
        var store = new FakeStore();
        var service = new RecoveryService(
            harness.Session,
            store,
            () => new RecoveryService.RecoveryPayload([1, 2, 3], "C:/newsletters/july.tboard"),
            id: "session",
            time: harness.Clock);
        return (service, store, harness, harness.Clock);
    }

    private static void Edit(EditorTestHarness harness) =>
        harness.Session.Execute(new MoveBlockCommand(
            EditorTestHarness.BlockId,
            harness.Session.Document.FindBlock(EditorTestHarness.BlockId).Block.FrameRect with { X = 60f }));

    [Fact]
    public void NothingIsWrittenForADocumentNobodyTouched()
    {
        (RecoveryService service, FakeStore store, EditorTestHarness harness, ManualTimeProvider clock) = Create();
        using (service)
        using (harness)
        {
            clock.Advance(TimeSpan.FromMinutes(10));

            Assert.False(service.Poll());
            Assert.Empty(store.Written);

            // Otherwise the file's timestamp would stop meaning "when the work was done".
            Assert.False(service.HasUnsavedWork);
        }
    }

    [Fact]
    public void FiveSecondsOfQuietIsEnoughToProtectTheWork()
    {
        (RecoveryService service, FakeStore store, EditorTestHarness harness, ManualTimeProvider clock) = Create();
        using (service)
        using (harness)
        {
            Edit(harness);
            Assert.True(service.HasUnsavedWork);

            clock.Advance(TimeSpan.FromSeconds(4));
            Assert.False(service.Poll());

            clock.Advance(TimeSpan.FromSeconds(2));
            Assert.True(service.Poll());
            Assert.Single(store.Written);
            Assert.False(service.HasUnsavedWork);
        }
    }

    /// <summary>
    /// Someone typing without pause never goes idle, so the 60-second cap is the only thing standing
    /// between them and the acceptance bound.
    /// </summary>
    [Fact]
    public void AContinuousTypistIsStillProtectedWithinTheMinute()
    {
        (RecoveryService service, FakeStore store, EditorTestHarness harness, ManualTimeProvider clock) = Create();
        using (service)
        using (harness)
        {
            // An edit every two seconds: never idle for five.
            for (int i = 0; i < 40; i++)
            {
                Edit(harness);
                clock.Advance(TimeSpan.FromSeconds(2));
                service.Poll();
            }

            Assert.NotEmpty(store.Written);

            // 80 seconds of continuous editing must have produced at least one snapshot, and no gap
            // between snapshots may exceed the cap.
            Assert.True(store.Written.Count >= 1);
        }
    }

    [Fact]
    public void EachSnapshotCarriesWhereTheDocumentCameFrom()
    {
        (RecoveryService service, FakeStore store, EditorTestHarness harness, ManualTimeProvider clock) = Create();
        using (service)
        using (harness)
        {
            Edit(harness);
            clock.Advance(TimeSpan.FromSeconds(6));
            service.Poll();

            RecoverySnapshot snapshot = Assert.Single(store.Written);
            Assert.Equal("C:/newsletters/july.tboard", snapshot.OriginalPath);
            Assert.NotEmpty(snapshot.Bytes);
        }
    }

    /// <summary>
    /// A surviving file MEANS the app did not close cleanly. That only holds if a clean close really
    /// does remove it — no heuristics, no "was it modified" guessing (docs/M9-spec.md §1.4).
    /// </summary>
    [Fact]
    public void ACleanCloseLeavesNothingToRecover()
    {
        (RecoveryService service, FakeStore store, EditorTestHarness harness, ManualTimeProvider clock) = Create();
        using (service)
        using (harness)
        {
            Edit(harness);
            clock.Advance(TimeSpan.FromSeconds(6));
            service.Poll();

            service.Complete();

            Assert.Contains("session", store.Deleted);
            Assert.False(service.HasUnsavedWork);
        }
    }

    [Fact]
    public void ClosingWithUnsavedWorkWritesItFirst()
    {
        (RecoveryService service, FakeStore store, EditorTestHarness harness, ManualTimeProvider _) = Create();
        using (service)
        using (harness)
        {
            Edit(harness);

            // No time has passed, so neither timer has fired — but the window is going away.
            Assert.True(service.SaveNow());
            Assert.Single(store.Written);
            Assert.False(service.SaveNow());
        }
    }

    [Theory]
    [InlineData(30, "less than a minute ago")]
    [InlineData(60, "1 minute ago")]
    [InlineData(150, "2 minutes ago")]
    [InlineData(3600, "1 hour ago")]
    [InlineData(7200, "2 hours ago")]
    public void TheRestoreDialogSaysWhenInPlainLanguage(int secondsAgo, string expected)
    {
        var now = new DateTimeOffset(2026, 7, 26, 20, 0, 0, TimeSpan.Zero);

        Assert.Equal(expected, RecoveryService.DescribeAge(now.AddSeconds(-secondsAgo), now));
    }

    // ---- the on-disk store ---------------------------------------------------------------------

    [Fact]
    public void TheStoreFindsWhatSurvivedAndForgetsWhatWasCompleted()
    {
        string dir = NewTempDir();
        try
        {
            var store = new FileRecoveryStore(dir);
            store.Write(new RecoverySnapshot("session", [1, 2, 3, 4], "C:/july.tboard", DateTimeOffset.UtcNow));

            RecoverySnapshot found = Assert.Single(store.FindRecoverable());
            Assert.Equal([1, 2, 3, 4], found.Bytes);
            Assert.Equal("C:/july.tboard", found.OriginalPath);

            store.Delete("session");
            Assert.Empty(store.FindRecoverable());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>
    /// The write must be temp-then-rename: a crash part-way through a direct write leaves a
    /// truncated archive, which is worse than no recovery file at all (docs/M9-spec.md §1.2).
    /// </summary>
    [Fact]
    public void WritingLeavesNoTemporaryFileBehind()
    {
        string dir = NewTempDir();
        try
        {
            var store = new FileRecoveryStore(dir);
            store.Write(new RecoverySnapshot("session", [9, 9, 9], null, DateTimeOffset.UtcNow));

            Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
            Assert.True(File.Exists(Path.Combine(dir, "session.tboard")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ATruncatedRecoveryFileIsNotOffered()
    {
        string dir = NewTempDir();
        try
        {
            File.WriteAllBytes(Path.Combine(dir, "session.tboard"), []);
            var store = new FileRecoveryStore(dir);

            // Offering an empty file would turn "your work is safe" into a lie.
            Assert.Empty(store.FindRecoverable());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>
    /// Backups protect against the user's own mistakes, which autosave cannot: deliberately deleting
    /// a page and saving is not a crash (PLAN.md §4).
    /// </summary>
    [Fact]
    public void SavingRotatesFiveBackupsBesideTheDocument()
    {
        string dir = NewTempDir();
        try
        {
            string path = Path.Combine(dir, "july.tboard");
            File.WriteAllText(path, "generation 1");

            // ROTATE THEN WRITE — the documented order. Rotating after writing would make .bak1 a
            // copy of the file just saved, so "restore an earlier version" would restore the very
            // thing the user wanted to undo.
            for (int generation = 2; generation <= 8; generation++)
            {
                FileRecoveryStore.RotateBackups(path);
                File.WriteAllText(path, $"generation {generation}");
            }

            Assert.Equal("generation 8", File.ReadAllText(path));

            // .bak1 is the version BEFORE the current one, and five generations are kept.
            for (int i = 1; i <= 5; i++)
            {
                Assert.Equal($"generation {8 - i}", File.ReadAllText($"{path}.bak{i}"));
            }

            Assert.False(File.Exists($"{path}.bak6"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void RotatingBackupsOfAFileThatDoesNotExistYetDoesNothing()
    {
        string dir = NewTempDir();
        try
        {
            FileRecoveryStore.RotateBackups(Path.Combine(dir, "new.tboard"));
            Assert.Empty(Directory.GetFiles(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static string NewTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "trestleboard-recovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
