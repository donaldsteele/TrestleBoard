#!/usr/bin/env python3
"""One-off generator for assets-src/fonts/fonts.json (M14).

The manifest is the checked-in source of truth; this script exists so the 59 face rows
were produced mechanically rather than typed. Re-run it only to re-shape the schema —
after a build, fonts.json carries SHA-256 hashes this script does not know about.
"""
import collections
import json

GF = "google-fonts"
SUB = "text-latin"

fam = []
faces = []


def F(family, folder, category, designer, source, licence, upstream_dir=None, blurb="",
      sample="", order=0, licence_name="SIL Open Font License 1.1"):
    fam.append(dict(family=family, folder=folder, category=category, designer=designer,
                    licenceFile=f"{folder}/{licence}", licence=licence_name,
                    source=source, upstreamDir=upstream_dir, description=blurb,
                    sampleText=sample, sortOrder=order))


def A(family, folder, weight, slant, out, source, path, subset=SUB, pins=None, frozen=False):
    faces.append(dict(family=family, weight=weight, slant=slant,
                      file=f"{folder}/{out}", source=source, upstreamPath=path,
                      axisPins=pins, subset=(None if frozen else subset), frozen=frozen,
                      sha256=""))


# --- Serif text -------------------------------------------------------------
F("Source Serif 4", "source-serif-4", "SerifText", "Frank Grießhammer",
  "adobe-source-serif", "LICENSE.md",
  blurb="The newsletter's usual reading face. Calm, roomy, easy on tired eyes.",
  sample="Stated Communication, second Tuesday", order=10)
for w, s, f_ in [("Regular", "Normal", "SourceSerif4-Regular.ttf"),
                 ("Bold", "Normal", "SourceSerif4-Bold.ttf"),
                 ("Regular", "Italic", "SourceSerif4-It.ttf"),
                 ("Bold", "Italic", "SourceSerif4-BoldIt.ttf")]:
    A("Source Serif 4", "source-serif-4", w, s, f_, "adobe-source-serif", f"TTF/{f_}", frozen=True)

serif_var = [
    ("EB Garamond", "eb-garamond", "ebgaramond", "EBGaramond", "Georg Duffner, Octavio Pardo",
     "An old-style book face. Warm and traditional — close to what a printed lodge notice "
     "used to look like.",
     "Brethren of Indian Land Lodge 414", 20, True),
    ("Crimson Pro", "crimson-pro", "crimsonpro", "CrimsonPro", "Jacques Le Bailly",
     "A slim book serif. Fits more words on a page without feeling cramped.",
     "Minutes of the previous communication", 30, True),
    ("Libre Baskerville", "libre-baskerville", "librebaskerville", "LibreBaskerville",
     "Impallari Type",
     "A sturdy, wide serif made for screens as well as paper. Very legible at small sizes.",
     "Fellowcraft degree at seven", 40, False),
    ("Lora", "lora", "lora", "Lora", "Cyreal",
     "A serif with brushed, calligraphic edges. Friendly without being informal.",
     "Widows and orphans committee report", 60, True),
    ("Libre Caslon Text", "libre-caslon-text", "librecaslontext", "LibreCaslonText",
     "Pablo Impallari",
     "The classic Caslon, cut for continuous reading. A very English, very old "
     "printing-house look.",
     "By order of the Worshipful Master", 70, True),
]
for family, folder, d, base, designer, blurb, sample, order, bi in serif_var:
    F(family, folder, "SerifText", designer, GF, "OFL.txt", d, blurb, sample, order)
    A(family, folder, "Regular", "Normal", f"{base}-Regular.ttf", GF,
      f"ofl/{d}/{base}[wght].ttf", pins={"wght": 400})
    A(family, folder, "Bold", "Normal", f"{base}-Bold.ttf", GF,
      f"ofl/{d}/{base}[wght].ttf", pins={"wght": 700})
    A(family, folder, "Regular", "Italic", f"{base}-Italic.ttf", GF,
      f"ofl/{d}/{base}-Italic[wght].ttf", pins={"wght": 400})
    if bi:
        A(family, folder, "Bold", "Italic", f"{base}-BoldItalic.ttf", GF,
          f"ofl/{d}/{base}-Italic[wght].ttf", pins={"wght": 700})

F("PT Serif", "pt-serif", "SerifText", "ParaType", GF, "OFL.txt", "ptserif",
  "A newspaper serif. Plain, even-coloured, and it never draws attention to itself.",
  "Refreshment at half past six", 50)
for w, s, src in [("Regular", "Normal", "Regular"), ("Bold", "Normal", "Bold"),
                  ("Regular", "Italic", "Italic"), ("Bold", "Italic", "BoldItalic")]:
    A("PT Serif", "pt-serif", w, s, f"PTSerif-{src}.ttf", GF,
      f"ofl/ptserif/PT_Serif-Web-{src}.ttf")

# --- Sans text --------------------------------------------------------------
F("Source Sans 3", "source-sans-3", "SansText", "Paul D. Hunt", "adobe-source-sans",
  "LICENSE.md",
  blurb="The plain, no-nonsense face used for captions and small print.",
  sample="Photographs by the Trestle Board committee", order=110)
A("Source Sans 3", "source-sans-3", "Regular", "Normal", "SourceSans3-Regular.ttf",
  "adobe-source-sans", "TTF/SourceSans3-Regular.ttf", frozen=True)
A("Source Sans 3", "source-sans-3", "Bold", "Normal", "SourceSans3-Bold.ttf",
  "adobe-source-sans", "TTF/SourceSans3-Bold.ttf", frozen=True)
A("Source Sans 3", "source-sans-3", "Regular", "Italic", "SourceSans3-It.ttf",
  "adobe-source-sans", "TTF/SourceSans3-It.ttf")
A("Source Sans 3", "source-sans-3", "Bold", "Italic", "SourceSans3-BoldIt.ttf",
  "adobe-source-sans", "TTF/SourceSans3-BoldIt.ttf")

sans_var = [
    ("Open Sans", "open-sans", "opensans", "OpenSans", "Steve Matteson",
     "A wide, open sans-serif. Probably the most familiar face on this list.",
     "Please bring a covered dish", 120, {"wdth": 100}, "wdth,wght"),
    ("Libre Franklin", "libre-franklin", "librefranklin", "LibreFranklin", "Impallari Type",
     "An American gothic sans with a printed, slightly condensed feel. Good for headings.",
     "Notice to all members", 140, None, "wght"),
]
for family, folder, d, base, designer, blurb, sample, order, extra, axes in sans_var:
    F(family, folder, "SansText", designer, GF, "OFL.txt", d, blurb, sample, order)
    for w, wv in (("Regular", 400), ("Bold", 700)):
        p = {"wght": wv}
        p.update(extra or {})
        A(family, folder, w, "Normal", f"{base}-{w}.ttf", GF, f"ofl/{d}/{base}[{axes}].ttf",
          pins=p)
    for w, wv, out in (("Regular", 400, "Italic"), ("Bold", 700, "BoldItalic")):
        p = {"wght": wv}
        p.update(extra or {})
        A(family, folder, w, "Italic", f"{base}-{out}.ttf", GF,
          f"ofl/{d}/{base}-Italic[{axes}].ttf", pins=p)

F("Lato", "lato", "SansText", "Łukasz Dziedzic", GF, "OFL.txt", "lato",
  "A humanist sans with soft, rounded shapes. Reads as warm rather than corporate.",
  "Fraternal greetings from the East", 130)
for w, s, src in [("Regular", "Normal", "Regular"), ("Bold", "Normal", "Bold"),
                  ("Regular", "Italic", "Italic"), ("Bold", "Italic", "BoldItalic")]:
    A("Lato", "lato", w, s, f"Lato-{src}.ttf", GF, f"ofl/lato/Lato-{src}.ttf")

F("PT Sans", "pt-sans", "SansText", "ParaType", GF, "OFL.txt", "ptsans",
  "A compact, businesslike sans. Useful when a table has more columns than room.",
  "Dues are payable in December", 150)
for w, s, src in [("Regular", "Normal", "Regular"), ("Bold", "Normal", "Bold"),
                  ("Regular", "Italic", "Italic"), ("Bold", "Italic", "BoldItalic")]:
    A("PT Sans", "pt-sans", w, s, f"PTSans-{src}.ttf", GF,
      f"ofl/ptsans/PT_Sans-Web-{src}.ttf")

# --- Display ----------------------------------------------------------------
F("Cinzel", "cinzel", "Display", "Natanael Gama", GF, "OFL.txt", "cinzel",
  "Roman inscription capitals. The masthead face this newsletter already uses.",
  "TRESTLE BOARD", 210)
A("Cinzel", "cinzel", "Regular", "Normal", "Cinzel-Regular.ttf", GF,
  "ofl/cinzel/Cinzel[wght].ttf", frozen=True)
A("Cinzel", "cinzel", "Bold", "Normal", "Cinzel-Bold.ttf", GF,
  "ofl/cinzel/Cinzel[wght].ttf", pins={"wght": 700})

display = [
    ("Playfair Display", "playfair-display", "playfairdisplay", "PlayfairDisplay",
     "Claus Eggers Sørensen",
     "High-contrast headline serif. Elegant, and best kept large.",
     "Annual Communication", 220),
    ("Cormorant Garamond", "cormorant-garamond", "cormorantgaramond", "CormorantGaramond",
     "Christian Thalmann",
     "A delicate display Garamond. Beautiful in a title, too fine for body text.",
     "Indian Land Lodge No. 414", 230),
    ("Bitter", "bitter", "bitter", "Bitter", "Sol Matas",
     "A slab serif with square, solid ends. Headings that hold up on a photocopy.",
     "This Month at the Lodge", 240),
]
for family, folder, d, base, designer, blurb, sample, order in display:
    F(family, folder, "Display", designer, GF, "OFL.txt", d, blurb, sample, order)
    for w, wv in (("Regular", 400), ("Bold", 700)):
        A(family, folder, w, "Normal", f"{base}-{w}.ttf", GF, f"ofl/{d}/{base}[wght].ttf",
          pins={"wght": wv})

F("Marcellus", "marcellus", "Display", "Astigmatic", GF, "OFL.txt", "marcellus",
  "Carved Roman letterforms with lowercase. Formal, and it has only one weight.",
  "Officers for the Ensuing Year", 250)
A("Marcellus", "marcellus", "Regular", "Normal", "Marcellus-Regular.ttf", GF,
  "ofl/marcellus/Marcellus-Regular.ttf")

# --- Accent -----------------------------------------------------------------
F("Cinzel Decorative", "cinzel-decorative", "Accent", "Natanael Gama", GF, "OFL.txt",
  "cinzeldecorative",
  "Cinzel with flourishes on the capitals. For a certificate line, not a paragraph.",
  "Fifty Year Award", 310)
A("Cinzel Decorative", "cinzel-decorative", "Regular", "Normal",
  "CinzelDecorative-Regular.ttf", GF, "ofl/cinzeldecorative/CinzelDecorative-Regular.ttf")
F("Great Vibes", "great-vibes", "Accent", "Robert Leuschke", GF, "OFL.txt", "greatvibes",
  "A formal joined script. Use it for a compliments line and nothing else.",
  "With fraternal regards", 320)
A("Great Vibes", "great-vibes", "Regular", "Normal", "GreatVibes-Regular.ttf", GF,
  "ofl/greatvibes/GreatVibes-Regular.ttf")
F("UnifrakturMaguntia", "unifraktur-maguntia", "Accent", "j. 'mach' wust", GF, "OFL.txt",
  "unifrakturmaguntia",
  "Blackletter, as seen on old charters and certificates. Hard to read in quantity — "
  "keep it short.",
  "Chartered 1948", 330)
A("UnifrakturMaguntia", "unifraktur-maguntia", "Regular", "Normal",
  "UnifrakturMaguntia-Regular.ttf", GF, "ofl/unifrakturmaguntia/UnifrakturMaguntia-Book.ttf")

names = [f["file"].split("/")[1] for f in faces]
dupes = [n for n, c in collections.Counter(names).items() if c > 1]
assert not dupes, dupes
assert len({(f["family"], f["weight"], f["slant"]) for f in faces}) == len(faces)
assert {f["family"] for f in fam} == {f["family"] for f in faces}

doc = {
    "$comment": [
        "One entry per FACE. The generated .ttf files are produced by tools/build-fonts.py and",
        "checked in; CI never runs Python, so Layout.Tests/FontManifestTests is the guarantee",
        "that what is on disk is what this file describes. See docs/M14-spec.md.",
        "'frozen': true means the bytes are upstream-verbatim and MUST NOT be re-subset —",
        "subsetting renumbers glyph ids and WidgetDrawListDump records them, which would",
        "re-bake every snapshot baseline and every widget golden.",
    ],
    "schemaVersion": 1,
    "sources": {
        "google-fonts": {
            "repo": "https://github.com/google/fonts",
            "commit": "7ff85c87f93ea6cca5f41c69f2e4edcb90240f26",
            "kind": "git-raw",
        },
        "adobe-source-serif": {
            "repo": "https://github.com/adobe-fonts/source-serif",
            "tag": "4.005R",
            "asset": "source-serif-4.005_Desktop.zip",
            "kind": "release-zip",
        },
        "adobe-source-sans": {
            "repo": "https://github.com/adobe-fonts/source-sans",
            "tag": "3.052R",
            "asset": "TTF-source-sans-3.052R.zip",
            "kind": "release-zip",
        },
    },
    "subsets": {
        "text-latin": {
            "unicodes": (
                "U+0020-007E,U+00A0-00FF,U+0131,U+0152-0153,U+0160-0161,U+0178,U+017D-017E,"
                "U+0192,U+02C6,U+02C7,U+02D8-02DD,U+2010-2015,U+2018-201A,U+201C-201E,"
                "U+2020-2022,U+2026,U+2030,U+2032-2033,U+2039-203A,U+2044,U+20AC,U+2113,"
                "U+2122,U+2126,U+212E,U+2212,U+2215"
            ),
            "layoutFeatures": "ccmp,locl,mark,mkmk,rlig,liga,kern,calt",
            "$comment": (
                "layoutFeatures is exactly what HarfBuzzShaper can turn on. It explicitly "
                "disables dlig and hlig, so retaining those would be dead weight."
            ),
        }
    },
    "headEpoch": 3660681600,
    "$headEpochComment": (
        "2020-01-01T00:00:00Z in TrueType epoch seconds (since 1904-01-01). head.created and "
        "head.modified are pinned to this, without which every run produces different bytes "
        "and the hashes below would be theatre."
    ),
    "families": fam,
    "faces": faces,
}
with open("assets-src/fonts/fonts.json", "w", encoding="utf-8", newline="\n") as fh:
    json.dump(doc, fh, indent=2, ensure_ascii=False)
    fh.write("\n")
print("families", len(fam), "faces", len(faces),
      "new", sum(1 for f in faces if not f["frozen"]))
