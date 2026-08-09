# M61 — Make it a list

**Delivered 2026-08-08.** PLAN.md §11 M61.

## 1. What it is for

The announcement with three points; the degree-night schedule; the dinner instructions. Today the
user types "1." and "•" by hand, and the alignment breaks the moment a line wraps: the second line
comes back to the left margin instead of sitting under the first word. Maintaining the numbering by
hand — insert a point in the middle, renumber the rest — is exactly the bookkeeping the app exists
to take away.

Reached the way Bold is reached: on the current paragraph or selection, no wizard, no dialog.

## 2. The number is counted, never stored

`StoryParagraph.ListKind` is `"bullet"`, `"number"`, or null. **The number of a numbered point is
not in the document.** It is counted at layout time from the run of numbered paragraphs immediately
above it.

That is what makes renumbering free: inserting a point in the middle renumbers everything after it
with **no edit to the document at all**, and a normal paragraph between two numbered ones starts the
count again, because two lists separated by prose are two lists. A stored number would be a second
copy of a fact the order already carries, and the two would disagree the first time somebody dragged
a paragraph — the same reasoning as M55's status-as-a-date and M60's derived column count.

## 3. The marker is not text

The bullet or number is shaped by the engine and emitted as a glyph run at the segment's left edge.
It is **not** in the story: not selectable, not deletable, never counted in a character offset. Had
it been real text, renumbering would mean rewriting the user's words, and "one Ctrl+Z puts the
paragraph back" would stop being true.

It carries a zero-width `SourceSpan` so the caret can never land inside it, and it is shaped in the
paragraph's own default run — so a bullet in a heading is heading-sized, which is what a printed
list does.

## 4. The hanging indent

`ParagraphStyle` gains `MarkerText`; the engine measures it once per paragraph and hangs **every**
line of that paragraph by its width, drawing the marker on the first line only. Wrapped lines
therefore align under the words rather than under the marker — the thing typing "1." by hand cannot
do, and the reason this milestone exists.

The indent is the marker's own measured width, not a fixed value, so a numbered point indents
slightly further than a bulleted one. `FirstLineIndentPt` is ignored for list paragraphs: a marker
replaces an indent rather than adding to one.

## 5. No baselines moved

PLAN.md budgeted "cross-OS snapshot baselines re-bake on all three platforms — a budgeted pixel
move, M18's rule." **None moved**, because no fixture uses a list: the setting defaults to null and
every existing document, sample and gallery is ordinary writing. Verified by re-baking with
`TRESTLEBOARD_UPDATE_BASELINES=1` — no diff in any of the fifteen Windows baselines.

That matters because only Windows can be baked here; Linux and macOS need the manual workflow and
the maintainer promoting artifacts. The budget was available and did not have to be spent.

## 6. The promise to every existing lodge

`ListKind` is nullable and null is never written, so **a newsletter written before M61 opens and
saves back byte-unchanged**. A property with a non-null default would have added
`"listKind": "none"` to every paragraph of every newsletter the committee has ever saved, the first
time they opened one. `ANewsletterWrittenBeforeThisMilestoneSavesBackByteUnchanged` holds it and
`ThePropertyIsNotWrittenAtAllForOrdinaryWriting` says why.

## 7. What guards it

- **`tests/Layout.Tests/ListParagraphTests.cs`** (12): the markers, counting up, renumbering after
  an insertion, restarting after a normal paragraph, bullets not continuing a numbered run; the
  hanging indent applying to every line; the marker sitting to the left of the writing; only the
  first line carrying one; the indent being the marker's own width; an ordinary paragraph laid out
  exactly as before; and **every bundled face being able to draw the bullet** — fonts are
  bundled-only with no system fallback, so a face missing U+2022 would print a missing-glyph box in
  one paragraph of somebody's newsletter, the hardest kind of fault to report. All twenty can.
- **`tests/Core.Tests/ListParagraphRoundTripTests.cs`** (8): the byte-unchanged promise, the
  property not being written for ordinary writing, a list surviving save/open, carry-forward
  clearing lists with the prose, `Clone` copying the setting, and the two kinds being the only ones
  known — `"bullet-level-2"` is not, which is where the one-level cap is written down.
- `CommandTests.EveryCommandTypeHasIdentityCoverage` demanded coverage for `SetListKindCommand` the
  moment it existed, so apply/revert/redo identity is held by the existing gate.

### Failure-first evidence

Applying the hanging indent to the first line only — the classic wrong implementation, and exactly
what typing by hand produces — failed `WrappedLinesLineUpUnderTheWritingRatherThanUnderTheMarker`,
`OnlyTheFirstLineCarriesAMarker` and `TheIndentIsTheMarkersOwnWidth`. Making the numbering skip
non-numbered paragraphs instead of stopping at them failed
`ANormalParagraphBetweenTwoListsStartsTheCountAgain` and `ARunOfPointsDoesNotContinueANumberedRun`.
Five named failures, and only those.

## 8. What was NOT done

- **One level only**, as PLAN.md requires. `ListKinds.IsKnown` rejects anything else, and nesting is
  bookkeeping this audience does not need — the cap is what keeps the two commands
  self-explanatory.
- **No icons.** The usual three-dots glyph and the usual 1-2-3 glyph are indistinguishable from each
  other at 20px, and from M60's table icon. Two sentences tell them apart; three near-identical
  pictures would not.
- **No "ordered" or "unordered" anywhere**, per §6. The commands say "Make this a list of points"
  and "Make this a numbered list", and putting one back says so plainly.
- **No hyphenation or justification rode along**, per §1's non-goals.

Suite after M61: **1477 passing, 12 skipped** (22 new). **No baseline moved.**
