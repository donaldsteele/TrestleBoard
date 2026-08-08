using System;
using System.IO;
using WeCantSpell.Hunspell;

namespace TrestleBoard.Spelling;

/// <summary>
/// The dictionary that ships inside the application (PLAN.md §11 M52).
///
/// <para>American English, generated from SCOWL by the LibreOffice dictionaries project — 49,568
/// words. It is an embedded resource for the reason the fonts are: a spell checker that works only
/// on a machine where somebody installed a dictionary is not offline, it is lucky.</para>
///
/// <para>The licence travels with it. The SCOWL word lists may be redistributed only on condition
/// that their copyright notice does, and the affix file carries Geoff Kuenning's BSD terms and
/// WordNet's on top, so the whole <c>README_en_US.txt</c> is embedded rather than an SPDX id.
/// <c>assets-src/dictionaries/dictionaries.json</c> records the SHA-256 of all three files and
/// <c>DictionaryManifestTests</c> fails if any of them changes without the manifest changing too
/// (PLAN.md §12 gate 22).</para>
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "A spelling dictionary is what this is, in the words the user and "
        + "PLAN.md §11 M52 both use. CA1711 reserves the suffix for IDictionary "
        + "implementations; renaming this to a word nobody says would cost more than "
        + "the rule protects.")]
public static class BundledDictionary
{
    private const string Prefix = "TrestleBoard.Spelling.Dictionaries.";

    /// <summary>The affix rules — how endings attach, so the word list need not spell out every form.</summary>
    public const string AffixResource = "en_US.aff";

    /// <summary>The word list itself.</summary>
    public const string WordsResource = "en_US.dic";

    /// <summary>What the licence requires us to hand over with the two files above.</summary>
    public const string LicenceResource = "README_en_US.txt";

    /// <summary>What the About-style surfaces call it.</summary>
    public const string Language = "English (United States)";

    private static readonly Lazy<WordList> Words = new(Load, isThreadSafe: true);

    /// <summary>
    /// The loaded dictionary. Lazy and shared: parsing half a megabyte of word list takes long
    /// enough to be noticed on an old machine, and nothing should pay for it until somebody asks
    /// about a word.
    /// </summary>
    public static WordList WordList => Words.Value;

    /// <summary>The dictionary's licence text, as shown by Help → "Fonts and licences".</summary>
    public static string ReadLicenceText()
    {
        using Stream stream = Open(LicenceResource);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static WordList Load()
    {
        using Stream words = Open(WordsResource);
        using Stream affix = Open(AffixResource);
        return WordList.CreateFromStreams(words, affix);
    }

    private static Stream Open(string name) =>
        typeof(BundledDictionary).Assembly.GetManifestResourceStream(Prefix + name)
        ?? throw new InvalidOperationException($"Embedded dictionary resource missing: {Prefix}{name}");
}
