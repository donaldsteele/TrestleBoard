using System.Text.Json;
using System.Text.Json.Serialization;
using TrestleBoard.Core.Model;

namespace TrestleBoard.Core.Container;

/// <summary>manifest.json (PLAN.md §2): format identity + version gates for migrations.</summary>
public sealed class TboardManifest
{
    public const string CurrentFormatName = "trestleboard";

    /// <summary>
    /// The newest format this build writes. M72 moved it off 1.0.0, where it had sat since M2, to
    /// make room for <see cref="VectorBlock"/> — the first live use of the migration chain.
    /// </summary>
    public const string CurrentFormatVersion = "1.1.0";

    /// <summary>
    /// What the format was called before M72, and what a newsletter with no drawing in it is still
    /// written as. See <see cref="RequiredVersionFor"/>.
    /// </summary>
    public const string BaseFormatVersion = "1.0.0";

    /// <summary>
    /// The version a drawing needs a reader to understand. A build that predates M72 binds
    /// <c>"type": "vector"</c> to nothing and would drop the block on the floor; refusing with
    /// "saved by a newer version of TrestleBoard" is the honest answer.
    /// </summary>
    public const string VectorFormatVersion = "1.1.0";

    public const string CurrentMinReaderVersion = BaseFormatVersion;

    /// <summary>
    /// The version THIS document actually needs, which is not the same as the newest this build can
    /// write (M72).
    ///
    /// <para>Stamping every save with 1.1.0 would mean a newsletter that has never contained a
    /// drawing changes its manifest the first time it is opened and saved by this build — and M61's
    /// rule is that a document saved by an older build round-trips byte-unchanged. It would also
    /// tell every older TrestleBoard to refuse a file it can read perfectly well. So the version is
    /// a function of the content: 1.1.0 when a drawing is on a page, 1.0.0 otherwise.</para>
    ///
    /// <para>M74 (d): "on a page" includes the page masters. A master's blocks render through the
    /// same block switch and carry-forward walks them, so a drawing on a master stamped 1.0.0 would
    /// let a pre-M72 reader past the version gate and into the polymorphic-deserialization crash
    /// the stamp exists to prevent.</para>
    /// </summary>
    public static string RequiredVersionFor(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.Pages.Any(page => page.Blocks.Any(block => block is VectorBlock))
            || document.PageMasters.Any(master => master.Blocks.Any(block => block is VectorBlock))
            ? VectorFormatVersion
            : BaseFormatVersion;
    }

    public string FormatName { get; set; } = CurrentFormatName;

    /// <summary>Semver of the document format this file was written in.</summary>
    public string FormatVersion { get; set; } = CurrentFormatVersion;

    /// <summary>Version of the app that wrote the file (informational).</summary>
    public string GeneratorVersion { get; set; } = "0.0.0";

    /// <summary>Oldest format version a reader must understand to open this file.</summary>
    public string MinReaderVersion { get; set; } = CurrentMinReaderVersion;

    public bool IsTemplate { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraProperties { get; set; }
}
