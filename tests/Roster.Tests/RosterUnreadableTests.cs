using TrestleBoard.Roster;
using Xunit;

namespace TrestleBoard.Roster.Tests;

/// <summary>
/// M24, PLAN.md §14.1 item 4: an address book that could not be READ must never be mistaken for an
/// address book that is EMPTY.
///
/// The two look identical to a caller — both hand back <see cref="RosterBook.Empty"/> — and the app
/// used to treat them the same. So a roster.json locked by antivirus or a sync client at the moment
/// the app started produced an empty book, and the next edit wrote that empty book over the real
/// one: real names, phone numbers and birthdays gone, with nothing said. Recovering meant knowing
/// the backup ring existed.
///
/// Every person here is fictional (PLAN.md §0 rule 2).
/// </summary>
public sealed class RosterUnreadableTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("tb-roster-unreadable-").FullName;

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

    private string RosterPath => Path.Combine(_folder, "roster.json");

    private static Member Placeholder(string id, string name) => new()
    {
        Id = id,
        DisplayName = name,
        BirthMonth = 3,
        BirthDay = 14,
        Phone = "555-0100",
        Email = "placeholder@example.invalid",
    };

    [Fact]
    public void NoFileYetIsDistinguishedFromAFileThatCouldNotBeRead()
    {
        var store = new RosterStore(RosterPath);

        Assert.Empty(store.Load(out RosterLoadState first).Members);
        Assert.Equal(RosterLoadState.NoFileYet, first);

        store.Save(RosterBook.Empty.With(Placeholder("person-1", "Aaron Placeholder")));
        Assert.Single(store.Load(out RosterLoadState second).Members);
        Assert.Equal(RosterLoadState.Loaded, second);

        File.WriteAllText(RosterPath, "{ this is not json");
        Assert.Empty(store.Load(out RosterLoadState third).Members);
        Assert.Equal(RosterLoadState.CouldNotBeRead, third);
    }

    /// <summary>
    /// The heart of it. A book that could not be read is a placeholder standing in front of real
    /// data, so every write is refused until it can be read — the file on disk is left exactly as
    /// it was found.
    /// </summary>
    [Fact]
    public void AnUnreadableBookIsNeverWrittenOver()
    {
        var store = new RosterStore(RosterPath);
        store.Save(RosterBook.Empty
            .With(Placeholder("person-1", "Aaron Placeholder"))
            .With(Placeholder("person-2", "Bertram Placeholder")));
        byte[] onDisk = File.ReadAllBytes(RosterPath);

        // Something outside the app leaves the file unreadable — a truncated write from a sync
        // client is the realistic shape of this.
        File.WriteAllText(RosterPath, "{\"members\": [ truncated");
        byte[] damaged = File.ReadAllBytes(RosterPath);

        var service = new RosterService(store);
        Assert.True(service.CouldNotBeRead);
        Assert.Empty(service.Book.Members);
        Assert.Contains("could not read it", service.UnreadableReason, StringComparison.Ordinal);

        Assert.Throws<InvalidOperationException>(
            () => service.Save(Placeholder("person-3", "Cedric Placeholder"), "Add Cedric Placeholder"));
        Assert.Throws<InvalidOperationException>(() => service.Delete("person-1", "Remove somebody"));
        Assert.Throws<InvalidOperationException>(
            () => service.Replace(RosterBook.Empty, "Replace the whole book"));
        Assert.False(service.Undo());

        // Nothing the service did touched the file — not even to "repair" it.
        Assert.Equal(damaged, File.ReadAllBytes(RosterPath));

        // And once whatever held it lets go, the app recovers without a restart.
        File.WriteAllBytes(RosterPath, onDisk);
        Assert.True(service.TryReadAgain());
        Assert.False(service.CouldNotBeRead);
        Assert.Equal(2, service.Book.Count);

        service.Save(Placeholder("person-3", "Cedric Placeholder"), "Add Cedric Placeholder");
        Assert.Equal(3, new RosterStore(RosterPath).Load().Count);
    }

    /// <summary>
    /// M24: the write happens before the in-memory book believes it. The old order set the book and
    /// the undo slot first, so a save that threw left the People window showing an edit that had
    /// reached no disk and raised no Changed event.
    /// </summary>
    [Fact]
    public void AFailedWriteLeavesTheServiceSayingWhatIsActuallyOnDisk()
    {
        var store = new RosterStore(RosterPath);
        store.Save(RosterBook.Empty.With(Placeholder("person-1", "Aaron Placeholder")));

        var service = new RosterService(store);
        int changedRaised = 0;
        service.Changed += (_, _) => changedRaised++;

        // A directory where the temp file must go cannot be written to as a file.
        Directory.CreateDirectory(RosterPath + ".tmp");

        Assert.ThrowsAny<Exception>(
            () => service.Save(Placeholder("person-2", "Bertram Placeholder"), "Add Bertram Placeholder"));

        Assert.Equal(0, changedRaised);
        Assert.Equal(1, service.Book.Count);
        Assert.False(service.CanUndo);
        Assert.Equal(1, new RosterStore(RosterPath).Load().Count);
    }
}
