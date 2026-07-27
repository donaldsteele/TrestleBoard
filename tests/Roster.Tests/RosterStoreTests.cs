using TrestleBoard.Roster;
using Xunit;

namespace TrestleBoard.Roster.Tests;

/// <summary>
/// The address book on disk (PLAN.md §11 M12). This file holds data that exists nowhere else, so
/// the two properties that matter most are that a bad file never stops the app and that a save
/// always leaves the version it replaced behind.
///
/// Every person here is fictional (PLAN.md §0 rule 2).
/// </summary>
public sealed class RosterStoreTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("tb-roster-").FullName;

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
        Email = name.Replace(" ", ".", StringComparison.Ordinal).ToLowerInvariant() + "@example.invalid",
    };

    [Fact]
    public void AMissingFileReadsAsAnEmptyBook()
    {
        var store = new RosterStore(RosterPath);

        Assert.False(store.Exists);
        Assert.Empty(store.Load().Members);
    }

    /// <summary>
    /// The never-throw contract, copied deliberately from <c>AppSettings.Load</c>: losing a
    /// preference is a nuisance, refusing to start is not — and that goes double for the file
    /// holding the whole lodge.
    /// </summary>
    [Fact]
    public void ACorruptFileReadsAsAnEmptyBookRatherThanThrowing()
    {
        File.WriteAllText(RosterPath, "{ this is not json at all");

        Assert.Empty(new RosterStore(RosterPath).Load().Members);
    }

    [Fact]
    public void SaveThenLoadReturnsTheSamePeople()
    {
        var store = new RosterStore(RosterPath);
        RosterBook book = RosterBook.Empty
            .With(Placeholder("person-1", "A Placeholder"))
            .With(Placeholder("person-2", "B Placeholder") with { Office = "Worshipful Master" });

        store.Save(book);
        RosterBook reloaded = store.Load();

        Assert.Equal(2, reloaded.Count);
        Assert.Equal("A Placeholder", reloaded.Find("person-1")!.DisplayName);
        Assert.Equal("Worshipful Master", reloaded.Find("person-2")!.Office);
        Assert.Equal("3/14", reloaded.Find("person-1")!.BirthdayText);
    }

    /// <summary>Anything a newer TrestleBoard wrote survives a round trip through this one (PLAN.md §2).</summary>
    [Fact]
    public void UnknownPropertiesSurviveARoundTrip()
    {
        File.WriteAllText(
            RosterPath,
            """
            {
              "schemaVersion": 1,
              "members": [
                { "id": "person-1", "displayName": "A Placeholder", "lodgeNumber": 414 }
              ],
              "somethingNewer": true
            }
            """);

        var store = new RosterStore(RosterPath);
        RosterBook book = store.Load();
        store.Save(book);

        string written = File.ReadAllText(RosterPath);
        Assert.Contains("lodgeNumber", written, StringComparison.Ordinal);
        Assert.Contains("somethingNewer", written, StringComparison.Ordinal);
    }

    [Fact]
    public void EverySaveKeepsTheVersionItReplaced()
    {
        DateTimeOffset now = new(2026, 7, 27, 12, 0, 0, TimeSpan.Zero);
        var store = new RosterStore(RosterPath, () => now);

        store.Save(RosterBook.Empty.With(Placeholder("person-1", "A Placeholder")));
        Assert.Empty(store.Backups());

        now = now.AddMinutes(1);
        store.Save(store.Load().With(Placeholder("person-2", "B Placeholder")));

        IReadOnlyList<RosterBackup> backups = store.Backups();
        RosterBackup kept = Assert.Single(backups);
        Assert.Equal(1, kept.MemberCount);
        Assert.Equal(2, store.Load().Count);
        Assert.Equal(1, RosterStore.ReadBackup(kept.Path).Count);
    }

    [Fact]
    public void TheRingKeepsTenAndDropsTheOldest()
    {
        DateTimeOffset now = new(2026, 7, 27, 12, 0, 0, TimeSpan.Zero);
        var store = new RosterStore(RosterPath, () => now);

        for (int i = 1; i <= 15; i++)
        {
            store.Save(store.Load().With(Placeholder("person-" + i, $"Placeholder {i}")));
            now = now.AddMinutes(1);
        }

        IReadOnlyList<RosterBackup> backups = store.Backups();
        Assert.Equal(RosterStore.BackupsKept, backups.Count);

        // Newest first, and the oldest kept copy is the one from ten saves ago — which had 14
        // people in it, because the very first save had nothing to keep.
        Assert.True(backups[0].SavedAt > backups[^1].SavedAt);
        Assert.Equal(14, backups[0].MemberCount);
        Assert.Equal(5, backups[^1].MemberCount);
    }

    [Fact]
    public void ABookWithNonsenseInItLosesTheNonsenseNotTheApp()
    {
        File.WriteAllText(
            RosterPath,
            """
            {
              "schemaVersion": 1,
              "members": [
                { "id": "person-1", "displayName": "A Placeholder", "birthMonth": 13, "birthDay": 40 },
                { "id": "person-2", "displayName": "   " },
                { "id": "", "displayName": "No Identity" }
              ]
            }
            """);

        RosterBook book = new RosterStore(RosterPath).Load();

        Member kept = Assert.Single(book.Members);
        Assert.Equal("person-1", kept.Id);
        Assert.False(kept.HasBirthday);
    }

    [Fact]
    public void IdsAreNeverReused()
    {
        RosterBook book = RosterBook.Empty
            .With(Placeholder("person-1", "A Placeholder"))
            .With(Placeholder("person-2", "B Placeholder"));

        Assert.Equal("person-3", MemberIds.Next(book));

        // The gap left by a deleted person is NOT filled: a spreadsheet exported last month would
        // otherwise re-import onto whoever now holds that id.
        book = book.Without("person-1");
        Assert.Equal("person-3", MemberIds.Next(book));
    }
}
