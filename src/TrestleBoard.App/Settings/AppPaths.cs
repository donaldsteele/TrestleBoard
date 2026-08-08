namespace TrestleBoard.App.Settings;

/// <summary>
/// Every file the app keeps for itself, named in one place (M12).
///
/// Until M12 the two app-state paths — settings and recovery — each built
/// <c>&lt;AppData&gt;/TrestleBoard/…</c> for themselves. M12 adds a third, and that third holds real
/// members' names, birthdays, phone numbers and emails (PLAN.md §0 rule 5), which changes what this
/// type is for: <b><see cref="Root"/> is settable, so a harness can be pointed at a temporary
/// folder and be structurally unable to read the real address book.</b> M15's screenshot tool is the
/// caller that needs it — it runs on the maintainer's own machine, where the roster is real, and a
/// single screenshot of the People window would put real personal data in a public repository. A
/// rule in a document does not prevent that. Not being able to see the file does.
///
/// The default is unchanged from what M9 and M10 wrote, so an existing installation finds its
/// settings and its recovery snapshots exactly where it left them.
/// </summary>
public static class AppPaths
{
    private static string? _root;

    /// <summary>
    /// Where app state lives. Defaults to <c>&lt;AppData&gt;/TrestleBoard</c>; set it before anything
    /// reads it, and every path below follows.
    /// </summary>
    public static string Root
    {
        get => _root ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TrestleBoard");
        set
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            _root = value;
        }
    }

    /// <summary>The user's chrome preferences (M9).</summary>
    public static string SettingsFile => Path.Combine(Root, "settings.json");

    /// <summary>Autosave and crash recovery (PLAN.md §4).</summary>
    public static string RecoveryDirectory => Path.Combine(Root, "recovery");

    /// <summary>The lodge address book (M12). Real personal data — see PLAN.md §0 rule 5.</summary>
    public static string RosterFile => Path.Combine(Root, "roster.json");

    /// <summary>
    /// Words the user has told the spell checker are not mistakes (M52). Real personal data — most
    /// of them will be members' surnames, so PLAN.md §0 rule 7 applies exactly as rule 5 does to
    /// the file above.
    /// </summary>
    public static string PersonalDictionaryFile => Path.Combine(Root, "personal-dictionary.txt");

    /// <summary>
    /// Paragraphs the user saved to the phrase shelf (M54). Real personal data: a memorial the
    /// committee keeps will carry a real name, so §0 rule 7 applies here too.
    /// </summary>
    public static string PhraseShelfFile => Path.Combine(Root, "phrases.json");

    /// <summary>
    /// Templates the committee saved for themselves (M57). A directory, like the recovery store,
    /// because each one is a whole <c>.tboard</c>.
    ///
    /// <para>§0 rule 7: a user template carries the officers table and the cover, so it holds real
    /// names. It is a personal file in AppData, and exporting one goes through the save dialog to a
    /// path the user chose — never a default beside the repository.</para>
    /// </summary>
    public static string TemplatesDirectory => Path.Combine(Root, "templates");

    /// <summary>Puts the root back to the default. For tests that set it.</summary>
    public static void ResetRootToDefault() => _root = null;
}
