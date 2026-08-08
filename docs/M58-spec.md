# M58 — Read it back to me

**Delivered 2026-08-08.** PLAN.md §11 M58. The third and last station of M51's checklist.

## 1. The silent walk-through is the baseline, not the fallback

Errors the eye slides over, the ear catches — and today that means recruiting a second person to
read aloud. But the architectural decision that shapes everything here is the other half of PLAN.md's
sentence: *where no voice engine answers, it degrades to a silent "walk me through it" mode, which
is independently valuable and is the tested-everywhere baseline.*

So the feature was built silent-first. `ReadAloudSession` is a pure state machine over sentences
that has never heard of a loudspeaker; `Sentences` is BCL-only segmentation in Core. Between them
they decide everything the user experiences — where it starts, what "back" means at the end, what it
says about its own progress — and **no test in this milestone depends on audio**, because the thing
being tested genuinely does not.

A committee member reading one sentence at a time, with everything else out of the way, catches
things a whole page hides. That is worth shipping on its own, and on a bare Linux box it is what
ships.

## 2. Speech is a hand-off, like printing and mail

`ISpeaker` has three members and two implementations. `SystemSpeaker` uses `say` on macOS,
`spd-say` on Linux, and PowerShell's `System.Speech` on Windows; `SilentSpeaker` is a machine with
no voice.

The same reasoning as M53's print and M56's mail, a third time: the machine already has the voices
the user chose, at the speed they set. `Available` is asked once at construction so somebody with no
voices is told on the first screen rather than after pressing Play and hearing nothing — the window
is even titled differently ("Walk me through it") so the promise matches what will happen.

Linux is best-effort by design, matching §6's existing AT-SPI stance.

One detail worth its comment: on Windows the sentence is passed to PowerShell as a single-quoted
literal with its own quotes doubled, so "the Master's message" cannot end the string early.

## 3. Segmentation, and where it deliberately stops

A full stop, question mark or exclamation ends a sentence; a run of them counts once; a closing
quote or bracket belongs to the sentence it closes.

Abbreviations are the one concession, and a short list of them: `Bro.`, `Dr.`, `St.`, and single
capital initials. *"Bro. Placeholder gave the charge"* read as two sentences is a stumble in the
middle of every issue this lodge prints. What is **not** attempted is general abbreviation handling —
it fails in ways nobody can predict, and the cost of a wrong split is a pause in the wrong place,
not a wrong word.

## 4. The highlight is chrome

The sentence being read is a band behind the words, drawn in `PageCanvasControl` with Avalonia's own
primitives — the same half of that file as M47's margins and M52's squiggles, never inside
`PageDrawOperation`. It cannot reach the PDF, and no snapshot baseline moved.

Behind rather than around: an outline at this size cuts through the descenders of the line above and
reads as damage. The accent colour at low opacity is what the selection already uses, so it is a
signal the reader has learned.

## 5. One extraction

M52's spelling underline and this highlight both needed "where does this stretch of characters sit
on this page?". The logic came out of `SpellingService` into `TextGeometry` when the second caller
arrived — the same call M52 made about `TextReplacement`. Two copies would eventually disagree, and
a squiggle under one word with a band behind a different one is the app pointing two ways at once.

## 6. A dead branch found by writing the break first

`ReadAloudSession.Back()` special-cased the finished state so that "back" at the end landed on the
last sentence rather than one past it. Deliberately breaking it changed no test result — because
`Next()` already stops at `Count`, so plain subtraction lands in exactly the same place. The special
case had never done anything.

It is now `Math.Max(0, _at - 1)` with the discovery recorded in the doc comment. This is the fourth
milestone this session where writing the break first found something the tests alone did not: M51
and M53 found weak tests, M55 found a defect, and this found dead code.

## 7. What guards it

- **`tests/Core.Tests/SentencesTests.cs`** (13): full stops, questions, exclamations; a run of
  punctuation counting once; a paragraph with no ending punctuation still being something to read; the
  three abbreviations and the initial; closing quotes; every sentence knowing where it starts; the
  whole-newsletter walk in reading order; and a newsletter with no writing.
- **`tests/Editing.Tests/ReadAloudSessionTests.cs`** (10): it starts before the first sentence so
  nothing is said uninvited; stepping; finishing rather than sticking; back-at-the-end and
  back-at-the-start; the progress wording; restart; the empty newsletter; and a sentence carrying
  enough position for the page to be turned to it.
- **`tests/App.HeadlessTests/ReadAloudShellTests.cs`** (6): with no voice it walks and says so; each
  step shows the next sentence and back returns; the highlight appears and is cleared when the
  window closes; **nothing is spoken until asked**; §6 on every button; and the review offers it
  before the page-by-page look.

### Failure-first evidence

Removing the abbreviation rule failed all four `AnAbbreviationDoesNotEndASentence` /
`AnInitialDoesNotEndASentence` cases. The second attempted break — simplifying `Back()` — failed
nothing, which is how §6's dead branch was found.

## 8. What was NOT done

- **No pause and resume mid-sentence.** The OS commands speak a whole sentence or are killed; a
  pause that could only stop at sentence boundaries is what "Say that again" already is, under a
  name that promises less.
- **No speed or voice choice.** Both live in the operating system's own speech settings, where the
  user has already set them once for everything.
- **No reading of widget content.** The officers table and the birthday list are generated; the same
  call M52 made about not proofreading the computer.
- **No word-level highlight.** The sentence is the unit the whole feature moves by, and a word-level
  chase would need timing information the hand-off does not give us.

Suite after M58: **1419 passing, 12 skipped** (29 new). No baseline moved.
