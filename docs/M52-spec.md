# M52 — Catch my spelling

**Delivered 2026-08-08.** PLAN.md §11 M52. §13 recorded spell check as "recommended but
unscheduled" awaiting the owner's word since M15; the product-owner pass was that word.

## 1. What ships, and what it cost

- **WeCantSpell.Hunspell 7.0.1**, a from-scratch managed port of Hunspell rather than a P/Invoke
  wrapper. That was the deciding property, not the API: NHunspell and the libhunspell bindings would
  each have added a native library **per RID**, and a spell checker must never be the reason a
  platform stops working. MIT, and confined to one leaf project.
- **The en_US dictionary**, 49,568 words, generated from SCOWL by the LibreOffice dictionaries
  project (`5ef55f9`, version 2020.12.07), in `assets-src/dictionaries/`.
- **`TrestleBoard.Spelling`**, a new leaf: Core plus that one package. Like `TrestleBoard.Roster`, it
  gets its own assembly partly so §0 has somewhere to point.

**The dictionary's licence is not one licence**, which is why the whole `README_en_US.txt` ships
rather than an SPDX id: the word lists are Kevin Atkinson's under a permission grant conditioned on
the notice travelling with them, the affix file is Geoff Kuenning's under his BSD terms, and parts
derive from WordNet 1.6 under Princeton's. `assets-src/dictionaries/dictionaries.json` records the
upstream, the pin, and a SHA-256 of all three files — PLAN.md §12 gate 22, on the `fonts.json`
precedent — and `DictionaryManifestTests` fails both ways round: a manifest entry whose hash is
wrong, and a file on disk with no entry. Help → "Fonts and licences" now shows the dictionary's
terms under the fonts'.

## 2. The guarantee, and why it is not a snapshot test

PLAN.md asks for proof that squiggles never reach the PDF. The obvious test — export a page and look
— would pass today and would keep passing right up until the afternoon somebody added a spelling
colour to the renderer "just for the editor", because by then it would be checking a document that
happens to have no misspelling in it, or a code path that happens not to be taken.

So the guarantee is **structural**, and `TheCheckerCannotReachThePageTests` checks the structure:
`Core`, `Layout`, `Rendering`, `Export.Pdf`, `Widgets` and `Editing` do not reference
`TrestleBoard.Spelling`; the four projects that actually put ink on a page do not so much as mention
spelling; `App` is the only place the two halves meet; and the checker itself stays a leaf, so the
forbidden arrow cannot appear in the other direction either. A squiggle in the PDF would require
somebody to add a line to a csproj first, and that line fails the build.

The underline is drawn in `PageCanvasControl.DrawSpellingMarks`, in the Avalonia-primitives half of
that file — the same half as M47's margins — never inside `PageDrawOperation`, which shares its
renderers with `TrestleBoard.Rendering`. No snapshot baseline moved.

## 3. Decisions worth the words

**Dotted, not coloured.** M14's hand-changed-font mark is already a solid line in the same place.
Colour would then have been the only thing separating "this font was changed by hand" from "this
might be misspelled", and §6 bans colour as the only carrier of meaning. Two shapes of line say two
things to somebody who cannot tell the two colours apart.

**The wizard is the primary path, not the underline.** An underline says "something is wrong
somewhere near here" to a person who must then find it, right-click it and read a small menu —
three fine-motor operations §6 exists to avoid. A screen saying *"chruch" — did you mean church?*
with three big buttons is a question anyone can answer.

**Marks are recomputed when the page changes, when a newsletter opens, and when the caret leaves a
frame — never per keystroke.** Marks appearing and vanishing under a half-typed word is exactly the
jitter this audience does not need, and a whole page re-checked per character is work nobody asked
for. The dotted line is a reminder, not a running commentary.

**What the checker refuses to have an opinion about.** Anything with a digit (`2026`, `1st`,
`555-0100`), anything in capitals (`F&AM`, `OES`, `PM`), anything under two letters. This is the
wall-of-red problem: every one of those is correct, none is in a dictionary, and a checker that asks
about all of them is turned off within one issue.

**Names one keystroke away are suggested first.** A brother's surname misspelled by one letter is
the mistake that stings — he notices — and the general dictionary can never fix it because it has
never heard the name. `IsOneKeystrokeAway` is bounded at a single edit on purpose: a full edit
distance over two hundred surnames would offer half the lodge as a suggestion for anything. It only
ever offers words already in the personal dictionary, so it cannot invent a name.

**A dictionary that cannot be written says so.** `PersonalDictionary.CouldNotBeSaved` exists because
accepting "never ask again" and then asking again next month, with no word said, is the kind of
small betrayal that makes somebody stop trusting a program.

**Widget payloads are not checked**, matching `StoryFinder`'s decision. The officers table and the
birthday list are filled in from the address book; asking the user whether a surname there is
spelled right is asking them to proofread the computer.

**The seed keeps only what the dictionary rejects.** SCOWL turns out to know "Worshipful", "Senior"
and "Grand"; keeping them would make the personal dictionary a list of words we guessed at rather
than words we needed. `LodgeVocabulary` is vocabulary only — §0 rule 2 — and not one real name.

## 4. Reuse rather than a second copy

`FindController` has built "delete these characters, put those there, as one undo step" since M21,
and the spell check needs exactly it. It moved to `Editing/TextReplacement.cs` and both call it. Two
copies of that rule would eventually disagree, and the day they did, one of the two features would
start needing two presses of Ctrl+Z to take back one thing the user did.

M51 was scheduled first so this could **add a station** to its checklist rather than build a second
review. `ReviewChecklist.Build` grew an `extraStations` parameter; the station itself is built in
the shell, because `Editing` does not reference `Spelling` and must not (§2). One screen — *"there
are eleven words I do not know"* — with the wizard behind a button, because eleven words inside the
review would bury the six questions the review is for.

## 5. Privacy (§0 rule 7)

The personal dictionary is real personal data. "It's a name — never ask again" is how most of the
checker's questions get answered, so within a month the file is a list of the lodge's surnames. It
lives at `%AppData%/TrestleBoard/personal-dictionary.txt`, `personal-dictionary*.txt` is gitignored,
every fixture name in `tests/Spelling.Tests` is fictional, and the address-book names it learns from
travel from the roster to that file and nowhere else. It is plain text rather than JSON because it
is a file a person may reasonably open in Notepad to take a word back out, and asking them to mind
the commas would be the sort of small cruelty this app exists to avoid.

## 6. What guards it

- **`tests/Spelling.Tests`** (42): the dictionary is inside the assembly and its licence with it;
  ordinary English is left alone and real typos are caught; the seven things that are not spelling
  are not asked about; the seed, the persistence across a restart, and the unwritable-file case;
  suggestions including the surname rule and the one-keystroke boundary cases; the walk finding the
  word, its offset and its sentence; apostrophes not splitting a word; widgets not proofread; the
  walk not touching the document. Plus the manifest gate and the four architecture tests of §2.
- **`tests/App.HeadlessTests/SpellingShellTests.cs`** (7): the wizard opening, a correction being
  one undo step, a word that has moved refusing to be changed blindly, the underline toggle saying
  "never prints", the M51 station appearing only when there is something to ask, §6 on every button,
  and the nothing-wrong wording.

### Failure-first evidence, honestly

Weaker than M51's, and worth stating plainly. Removing the digit guard from `IsWorthChecking`
produced one clean named failure — `ThingsThatAreNotSpellingAreNotAskedAbout(word: "1st")` — and only
one, because `"2026"` and `"555-0100"` are still caught by the has-a-letter rule further down. The
digit guard is therefore partly belt-and-braces, and only `"1st"` genuinely needs it.

A second attempted break was **refused by the compiler**: removing the personal dictionary from
`IsSpelledRight` left the method not touching instance data, and `CA1822` is an error here. That is
a real guard rail rather than evidence, and it is recorded as what it is.

## 7. What was NOT done

- **No grammar check, and no "form" versus "from".** The nothing-wrong screen says so in as many
  words, because "nothing is misspelled" and "everything is right" are different claims and the
  difference matters to somebody about to send six pages to sixty people.
- **No right-click "did you mean" menu on the underline.** That is the three-fine-motor-operations
  path §6 exists to avoid; the wizard is the answer.
- **No second language.** One dictionary, named in the manifest, with room for more entries beside
  it when a lodge needs one.
- **No re-check while typing.** See §3.

Suite after M52: **1304 passing, 12 skipped** (49 new, and a ninth test project). No baseline moved,
no screenshot re-baked.
