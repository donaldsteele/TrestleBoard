# M98 — Three everyday verbs

**Delivered 2026-09-05.** Changing capitals, counting words, and putting in a character the keyboard
has no key for.

## What was wrong

Three things every word processor has had for thirty years, and this had none of:

- **Change case.** A heading arrives from a Word document IN CAPITALS, or a name is typed in lower
  case at half past ten at night. The only remedy was retyping it — which loses the styling on it,
  and this audience types slowly.
- **Word count.** A count existed for *imported* files (`ImportedWriting.WordCount`) and for
  *overset* text, and never for what is actually on the page. "Is the Master's message long enough?"
  had no answer.
- **Special characters.** A long dash, a degree sign, ½ — marks with no key on the keyboard. The
  committee pasted them in from somewhere else or did without.

## What shipped

`text.changeCase`, `text.wordCount` (both in a new **More about the te*x*t** submenu in Format) and
`insert.symbol` ("A character yo*u* cannot type…").

## The decisions

**Change case keeps the styling.** The words are replaced **run by run**, each with its own
character style put back, rather than deleted and retyped as one plain string. A bold word inside
the highlight is still bold afterwards, and a test holds it. The spans are walked back to front,
because every edit before a run would shift the offsets of the ones after it.

**Title case is not `TextInfo.ToTitleCase`.** That method leaves a word already in capitals alone —
"THE STATED COMMUNICATION" comes back unchanged — and that is precisely the input somebody reaches
for this command to fix. The app's own rule uppercases the first letter of each word and lowercases
the rest, with an apostrophe **not** starting a new word so "O'Brien" does not become "O'brien".
Both spellings of apostrophe are handled, because the symbol picker inserts the curly one.

**"About" is honest rather than modest.** A word count is a count of whitespace runs, and every
program disagrees about hyphens, ampersands and "St." — saying *about* means nobody has to wonder
why this number differs from the one Word gave them. `WordsIn` is the single definition behind every
number the app says.

**Paragraph breaks are not letters.** They are structure; nobody asking how long an article is means
to count the gaps between its paragraphs.

**A short named list, not a Unicode table.** Every other program offers several thousand glyphs
organised by a scheme only typographers know. A trestle board needs about a dozen marks, and §6 asks
that each be *named* — "a long dash" tells somebody what they are choosing in a way that seeing "—"
at 11 point does not. Every character offered is in the bundled fonts, because the font rule (M14)
is bundled-only and a picker that could insert a missing glyph would produce a box in the PDF with
no warning anywhere.

**The symbol goes in through `InsertText`**, so it is one ordinary typed character as far as
everything downstream is concerned — undo, coalescing, styling and the spell checker included.

## Not built, and why

**Paste-special.** The register listed "paste as plain text" as a gap. It is not one: paste in this
app is *always* plain — `ITextClipboard` is a string interface and `PasteText` sanitises — so a
"keep formatting / discard formatting" choice would offer the user a decision with one real answer.
The *file* route (`insert.writingFromFile`) does map Word's paragraph styles onto the app's, which is
the case where the choice would mean something, and it already takes it.

**Format painter** and **soft line break** remain open. The second needs the layout engine to learn a
within-paragraph break, which is a change to `LineBreakAnalyzer` and therefore to every baseline's
risk surface; it wants its own milestone rather than a corner of this one.

## Two menus nearly out of access keys

Format had six letters left and Insert six, and none of them appeared in any natural wording of
these commands. Change case and word count went into a **submenu** — the third time this session
that has been the answer — and the symbol picker was reworded to "A character yo**u** cannot type…",
which is plainer than what it replaced anyway.

That Format now needs a second submenu is worth recording as a finding about the menu rather than
about these commands.

## Verification

`dotnet test TrestleBoard.slnx` — all green, no snapshot baseline moved. Eighteen new tests in
`tests/Editing.Tests/EverydayVerbTests.cs`, including the shouting-comes-back-down case that
`ToTitleCase` fails, the apostrophe rule, and the styling-survives guarantee.
