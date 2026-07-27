#!/usr/bin/env python3
"""Emit src/TrestleBoard.Layout/Fonts/BundledFontCatalog.cs from fonts.json (M14).

PLAN.md M14 rejects a Roslyn source generator for this — the catalog is a hand-maintainable
C# file. This script exists so the 79 rows were typed once by a machine rather than by hand;
after that the .cs file is ordinary source and may be edited directly. If you edit it, edit
this script too, or the next run will quietly undo you.
"""
import json
from pathlib import Path

FONTS = Path(__file__).resolve().parent.parent
OUT = FONTS.parent.parent / "src" / "TrestleBoard.Layout" / "Fonts" / "BundledFontCatalog.cs"

CATEGORY_LABELS = {
    "SerifText": "Fonts for reading",
    "SansText": "Plain fonts, without the little feet",
    "Display": "Fonts for titles and headings",
    "Accent": "Fancy fonts, for a line or two",
}
CATEGORY_ORDER = ["SerifText", "SansText", "Display", "Accent"]


def cs(text: str) -> str:
    return '"' + text.replace("\\", "\\\\").replace('"', '\\"') + '"'


def main() -> None:
    manifest = json.loads((FONTS / "fonts.json").read_text(encoding="utf-8"))
    families = sorted(manifest["families"], key=lambda f: f["sortOrder"])
    order = {f["family"]: f["sortOrder"] for f in families}
    face_rank = {("Regular", "Normal"): 0, ("Bold", "Normal"): 1,
                 ("Regular", "Italic"): 2, ("Bold", "Italic"): 3}
    faces = sorted(manifest["faces"],
                   key=lambda f: (order[f["family"]], face_rank[(f["weight"], f["slant"])]))

    lines = []
    add = lines.append
    add("// Emitted once by assets-src/fonts/tools/gen-font-catalog.py from")
    add("// assets-src/fonts/fonts.json, then maintained by hand. PLAN.md M14 deliberately")
    add("// rejects a source generator here: it would trade this table for a Roslyn project,")
    add("// a build-order dependency and a debugging failure mode nobody on this project has.")
    add("// Layout.Tests/FontCatalogTests holds the two halves together — set-equality in BOTH")
    add("// directions between this table and the assembly's embedded resources.")
    add("namespace TrestleBoard.Layout.Fonts;")
    add("")
    add("/// <summary>How the font picker groups the bundled families. Order is display order.</summary>")
    add("public enum FontCategory")
    add("{")
    for name in CATEGORY_ORDER:
        add(f"    {name},")
    add("}")
    add("")
    add("/// <summary>")
    add("/// One bundled family, with the two things the picker cannot invent: a plain-language")
    add("/// description an elderly reader can act on, and a sample line worth setting in it.")
    add("/// </summary>")
    add("public sealed record FontFamilyInfo(")
    add("    string Family,")
    add("    FontCategory Category,")
    add("    string Description,")
    add("    string SampleText,")
    add("    int SortOrder);")
    add("")
    add("/// <summary>One bundled face and the embedded resource its bytes come from.</summary>")
    add("public readonly record struct BundledFace(FontKey Key, string Resource);")
    add("")
    add("public static class BundledFontCatalog")
    add("{")
    add("    private static readonly FontFamilyInfo[] FamilyTable =")
    add("    [")
    for family in families:
        add(f"        new FontFamilyInfo(")
        add(f"            {cs(family['family'])},")
        add(f"            FontCategory.{family['category']},")
        add(f"            {cs(family['description'])},")
        add(f"            {cs(family['sampleText'])},")
        add(f"            {family['sortOrder']}),")
    add("    ];")
    add("")
    add("    private static readonly BundledFace[] FaceTable =")
    add("    [")
    for face in faces:
        key = (f"new FontKey({cs(face['family'])}, FontWeight.{face['weight']}, "
               f"FontStyleSlant.{face['slant']})")
        resource = face["file"].split("/")[1]
        add(f"        new BundledFace(")
        add(f"            {key},")
        add(f"            {cs(resource)}),")
    add("    ];")
    add("")
    add("    /// <summary>Every bundled family, in the order the picker lists them.</summary>")
    add("    public static IReadOnlyList<FontFamilyInfo> Families => FamilyTable;")
    add("")
    add("    /// <summary>Every bundled face, grouped by family in <see cref=\"Families\"/> order.</summary>")
    add("    public static IReadOnlyList<BundledFace> Faces => FaceTable;")
    add("")
    add("    /// <summary>Family names only, in display order. The set a document is audited against.</summary>")
    add("    public static IReadOnlyList<string> FamilyNames { get; } =")
    add("        FamilyTable.Select(f => f.Family).ToArray();")
    add("")
    add("    private static readonly Dictionary<string, FontFamilyInfo> ByName =")
    add("        FamilyTable.ToDictionary(f => f.Family, StringComparer.Ordinal);")
    add("")
    add("    /// <summary>True when this build bundles the named family.</summary>")
    add("    public static bool Contains(string family) => ByName.ContainsKey(family);")
    add("")
    add("    /// <summary>The family's metadata, or null when this build does not bundle it.</summary>")
    add("    public static FontFamilyInfo? Find(string family) =>")
    add("        ByName.TryGetValue(family, out FontFamilyInfo? info) ? info : null;")
    add("")
    add("    /// <summary>The families in one picker group, in display order.</summary>")
    add("    public static IEnumerable<FontFamilyInfo> InCategory(FontCategory category) =>")
    add("        FamilyTable.Where(f => f.Category == category);")
    add("")
    add("    /// <summary>The faces one family bundles, in Regular/Bold/Italic/BoldItalic order.</summary>")
    add("    public static IEnumerable<BundledFace> FacesOf(string family) =>")
    add("        FaceTable.Where(f => f.Key.Family == family);")
    add("")
    add("    /// <summary>The plain-language heading the picker puts above a group (PLAN.md §6).</summary>")
    add("    public static string CategoryLabel(FontCategory category) => category switch")
    add("    {")
    for name in CATEGORY_ORDER:
        add(f"        FontCategory.{name} => {cs(CATEGORY_LABELS[name])},")
    add("        _ => \"Other fonts\",")
    add("    };")
    add("}")
    OUT.write_text("\n".join(lines) + "\n", encoding="utf-8", newline="\n")
    print(f"wrote {OUT} — {len(families)} families, {len(faces)} faces")


if __name__ == "__main__":
    main()
