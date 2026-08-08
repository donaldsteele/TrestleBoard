# M51 — Look it over with me

**Delivered 2026-08-08.** PLAN.md §11 M51, the first of the product-owner pass.

## 1. What it is for

Proofreading six pages is the hardest task in the committee's month, and it is hardest for exactly
the eyes doing it. Answering "is this right?" eight times is a different job from finding eight
things wrong, and it is a job this audience can finish.

So "Look it over with me…" walks the newsletter top to bottom and turns it into a short list of
questions, one to a screen, each with a way to go and look and a way to say that is fine.

## 2. What it looks for

Seven stations, in the order the reader meets them on the page:

| Kind | What it notices |
|---|---|
| `UnwrittenWords` | A story still holding the sentence the template put there |
| `WritingThatRanOut` | M43's overset, asked about rather than badged |
| `EmptyPictureFrame` | A picture box with nothing in it |
| `PictureWithoutCaption` | Nothing printed under a picture |
| `PictureWithoutDescription` | Nothing for a reader who cannot see it |
| `DateFromAnotherMonth` | Last month's month named in this month's writing |
| `LookAtThePage` | One screen per page, last, because no checklist replaces looking |

## 3. Three judgement calls, and why

**The date check only looks backwards.** A September newsletter announcing the October picnic is
doing its job; flagging it would fire the check on nearly every issue, and a checklist that cries
wolf is one the committee stops running. Only the *previous* month is asked about — the month that
"last month's file with the date changed" leaves behind. Month names are matched on whole words,
because "May" inside "Maybe" and "March" inside "marching" would otherwise fire every year.

**An empty picture box is one question, not three.** The caption and the description are questions
*about a picture*, and there is no picture yet. Asking all three would be three screens saying the
same thing to somebody who has already understood it.

**Everything is asked, nothing is asserted.** PLAN.md requires it of the stale-date heuristic; it is
the manner of the whole window. An app that tells a volunteer their newsletter is wrong is an app
they stop opening. `EveryQuestionIsAQuestion` holds the literal property — every finding's text ends
in a question mark.

## 4. The defect found on the way in

`ActionContextFactory.HoldsAPrompt` looked for `CarryForward.DefaultArticlePrompt` and nothing else.
There are **seven** placeholder prompts; each of the three templates owned its own `internal` copy of
the ones it uses, and six of the seven were matched by no code anywhere.

The visible consequence: a newsletter started from a template — whose cover says *"Write the
Worshipful Master's message here…"* and whose photo pages say two other things — was reported as
having no prompts left. `ActionContext.HasUnwrittenArticle` was false and the What's Next card that
exists to say *"you have not written the article yet"* stayed quiet, on the one route where nothing
had been written at all. It only ever worked for start-from-last-month.

The strings now live in one public place, `Core.Templates.PlaceholderPrompts`, and the templates
and `CarryForward` name them from there. `HoldsAPrompt` matches all seven. This is the same class of
finding as M39's and M50's: **something that reported what it was asked, on one of the paths it was
asked about.**

`PlaceholderPrompts` also settles a trap the checklist would otherwise have walked into. The
six-page template ships its prompt **inside `ImageFrame.AltText`**, so the obvious
`!string.IsNullOrWhiteSpace(AltText)` test calls every untouched photo frame described.
`NeedsDescription` excludes the prompt, and a test holds it.

## 5. Read-only, and how that is kept true

PLAN.md's acceptance is that the checklist is read-only over the document. It is kept true
structurally rather than by care:

- `ReviewChecklist.Build` takes a `Document` and two collections of block ids and returns records.
  It holds no session, runs no command, and cannot reach one. `NothingTheChecklistDoesTouchesTheNewsletter`
  serialises the document either side of a run and compares.
- The two sets it needs from the laid-out document — the overset tails and the picture frames whose
  bytes do not decode — are **passed in** by the shell, which already has them from
  `DocumentRenderSource`. A checklist that could trigger a relayout would be doing more than reading.
- **"Do not ask me again" lives in `AppSettings`, not the newsletter.** This is the one place M43's
  "I know" pattern is deliberately *not* copied. M43 put its dismissal in the document because it is
  about one picture in one issue; this is a preference about how a person likes to work, it should
  outlive any one issue, and writing it into the `.tboard` would mark the newsletter as edited on
  the way to exporting it.

## 6. The offer, which is only an offer

"Make the PDF" asks first: **Look it over with me** / **Make the PDF now** / **Make it now, and stop
asking**. The hook sits at the top of `ExportPdfAsync`, after the null guard and before the file
picker, which is the same position the unsaved-changes question occupies.

The only way it can stop an export is the user choosing to look it over — which is them changing
their mind, not the app refusing. `SayingMakeItNowLeavesTheExportAlone` holds that.

## 7. The window

`ReviewWindow` is **not modal**, for the same reason `FindWindow` is not: every screen is about
something on the page behind it, and "Take me there" would be a lie if the user were not allowed to
touch the page it took them to.

It borrows `WizardWindow`'s visual language — 20pt, one thing per screen, a big Back and a big Next,
a count so nobody is lost, the heading focused on each screen so a screen reader reads the new
question — and none of its machinery, because the questions are discovered from the newsletter at
runtime rather than declared in advance. `RosterImportWindow` made the same call for the same
reason.

Screen 0 is a summary, and it is the screen that matters when there is nothing wrong: *"I went
through the newsletter and nothing jumped out at me. That is not the same as it being right…"*.
Past the last screen the window closes rather than offering a Finish, because nothing was changed
and there is nothing to finish.

"Take me there" turns the page **first**, then selects — `GoToPage` clears the selection, so the
other order destroys the very selection the button exists to make. That is M21's lesson, and the
comment says so at the call site.

Remedies are offered by their own name from the catalog — "Make the rest fit", "Put a picture
here…" — never as a generic "fix it", so the button in the review says the same words as the button
everywhere else.

## 8. What guards it

**`tests/Editing.Tests/ReviewChecklistTests.cs`** (21 tests, no window): PLAN.md's fixture criterion
made literal — a two-page document seeded with one of each defect surfaces all seven kinds — plus
the month-boundary cases (January looks back at December; October is not asked about in September;
"Maybe" and "marching" are not months), the one-question-per-story rule across a three-frame chain,
the prompt-left-below-somebody's-writing case, the empty-box-is-one-question rule, and the
read-only proof.

**`tests/App.HeadlessTests/ReviewShellTests.cs`** (7 tests): the offer in all three answers,
"stop asking" persisting and the export still going ahead, "Take me there" landing on the right page
with the right frame chosen, the screens walking forwards and backwards and ending by closing, the
nothing-wrong wording, and §6 on every button on every screen (44 high, 18pt, something to say to a
screen reader).

The M11 surface gates covered `newsletter.review` unprompted the moment the id existed —
`MenuIndexTests` caught the access-key clash with "Start from _last month" before a human did.

### Failure-first evidence

Two deliberate breaks — `NeedsDescription` reduced to the naive null-or-whitespace test, and the
date check pointed at the *next* month instead of the previous one — produced **five named
assertion failures** and no others:
`ThePromptTheTemplatePutInTheDescriptionIsNotADescription`, `AMonthBeforeThisIssuesIsAskedAboutRatherThanAsserted`,
`JanuaryLooksBackAtDecember`, `AMonthAheadOfThisIssuesIsNotAskedAbout`, `AFixtureWithOneOfEachDefectSurfacesAllOfThem`.

This is the clean evidence M50 recorded itself as lacking, and the reason is worth noting: the
checklist's judgement lives in a pure function in `Editing`, so it can be broken and re-run in one
second without a shell session to hang.

## 9. What was NOT done

- **No spell check and no read-aloud.** They are M52 and M58, and both join this frame as further
  stations. The window takes its findings as a list, so adding one is adding a producer.
- **No "fix it all" button.** Every remedy is the command the user would have run themselves, one at
  a time, from the screen that explains why.
- **No icon.** Recorded in `ActionIcons.WithoutAnIcon`: every glyph that could mean "look it over" —
  an eye, a tick, a magnifier — already means something else here or reads as "this is finished",
  which is the one thing the review must not promise before it has run.
- **No widget content is checked.** The cover banner's meeting date is generated from a rule, and
  the officers table from the address book; a checklist second-guessing them would be asking the
  user about something the app is supposed to know.

Suite after M51: **1255 passing, 12 skipped** (29 new). No baseline moved, no screenshot re-baked,
and no file in `Layout`/`Rendering`/`Export.Pdf` was opened.
