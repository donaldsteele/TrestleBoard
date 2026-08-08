# M54 — Words for hard news

**Delivered 2026-08-08.** PLAN.md §11 M54.

> **⚠ The owner has not signed off the wording yet.** PLAN.md's acceptance says the tone of the
> shipped paragraphs is reviewed by the owner before shipping, because *this is lodge voice, not app
> voice*. What is committed is a draft for that review. Everything else in the milestone — the
> machinery, the shelf, the tests — is finished; §3 below is the part that needs a reading.

## 1. What it is for

The committee re-drafts a memorial every time one is needed, or digs through last year's issues to
copy the wording. Blank-page paralysis is worst under grief, and a dignified starting text with two
blanks to fill is a different task altogether from an empty frame.

Five paragraphs ship: a memorial notice, a sickness-and-distress entry, a get-well message, a
welcome to a newly raised brother, and a thank-you to a degree team.

## 2. How it works

`Insert → "Words for hard news…"` opens a modal wizard: choose a paragraph from the list, answer its
blanks one at a time, read the whole thing back, then put it in.

**Modal, unlike M51's and M52's windows.** Those point at the page behind them, so they had to leave
it reachable; this one asks a short series of questions and then does a single thing. That is the
wizard shape, and it takes the wizard's manners — one question per screen, 20pt, a big Back and a
big Next, and a look at the finished words before anything touches the newsletter.

**A blank may be left empty**, and that is deliberate: a memorial is often written before the date
is settled. What prints is then a line of underscores — something a person can see and fill in —
rather than `{date}`, which reads as the program having gone wrong. Two tests hold that.

**What arrives is ordinary editable writing.** The paragraph lands in the newsletter as words the
committee can change, and most of them will. It is a starting point, not a form letter.

## 3. The wording, for review

Each paragraph below is what a committee member will read. `{name}` and `{date}` are asked for one
at a time; nothing else is filled in.

- **A memorial notice** — *"It is with sorrow that we record the passing of Brother {name}, who was
  called to the Celestial Lodge above on {date}. He gave many years of faithful service to this
  lodge and to the craft, and his place among us will not easily be filled. The brethren extend
  their heartfelt sympathy to his family, and he will be remembered with affection whenever we
  meet."*
- **Sickness and distress** — *"Brother {name} is unwell at present, and the lodge holds him in its
  thoughts. Cards and visits would be welcome. If you would like to know how best to help, please
  speak to the Almoner, who is keeping in touch with the family."*
- **A get-well message** — *"The brethren send their warmest wishes to Brother {name} for a full and
  swift recovery. We look forward to welcoming him back to lodge before long."*
- **Welcome to a newly raised brother** — *"The lodge is pleased to welcome Brother {name}, who was
  raised to the Sublime Degree of Master Mason on {date}. We congratulate him on his progress
  through the degrees, and we look forward to his company at our meetings for many years to come."*
- **Thank you to a degree team** — *"The Worshipful Master extends the thanks of the lodge to the
  degree team who worked on {date}. Their preparation and their care did credit to the craft, and
  the evening will be remembered by all who were present."*

Three choices in there that are the owner's to confirm, not mine:

1. **"called to the Celestial Lodge above"** rather than "passed away" or "died". PLAN.md itself
   uses the phrase, and it is the one a lodge would print — but it is a jurisdictional usage and the
   owner should say whether it is this lodge's.
2. **The Almoner is named** in the sickness entry as the person to speak to. If this lodge routes
   that through the Secretary, the sentence is wrong for them.
3. **The memorial claims "many years of faithful service"**, which will occasionally be untrue of a
   brother who was raised recently. The alternative is a blander sentence that says less for
   everyone; the committee can edit either way, and a starting text that says something is the point.

## 4. Keeping your own words

"Keep these words for next time…" saves whatever is highlighted, under a name the user chooses. The
committee's own wording for a hard moment is usually better than ours, and next year they will want
it again.

Saved paragraphs appear after the shipped ones and never before them: a list that reorders itself as
you use it is a list you cannot learn, which matters more to this audience than to most. Saving
twice under one name replaces rather than quietly making a second copy nobody can tell apart. A
shelf file that is rubbish costs the saved words and not the evening, on the `AppSettings` pattern —
and if the write fails, the shell says so rather than pretending, which is M52's rule.

## 5. `InsertBlock`, and why `InsertText` was not enough

The explorer's map turned up a real problem before a line of the window was written: a bare
`InsertTextCommand` **coalesces with the typing either side of it**. That is right for typing, where
a burst of keystrokes is one thing the user did — and wrong here. Somebody who inserts a memorial,
types a sentence after it and presses Ctrl+Z means *take back the sentence*, not *take back the
memorial as well*.

So `TextEditorController.InsertBlock(text, undoLabel)` was added beside `InsertText`: the same
placement and caret behaviour, wrapped in a `CompositeCommand`, which does not merge. It also lets
the undo be named after the thing the user chose, which is what they will be looking for when they
change their mind. `TakingBackWhatYouTypedAfterwardsDoesNotTakeBackTheWords` is the test, and it
fails against a version that executes the children loose.

## 6. Privacy (§0 rules 2 and 7)

- **Shipped:** not one real name, date or lodge. Every place a person could be named is a blank, and
  `NoShippedParagraphNamesAnybody` holds it — a shipped paragraph naming a real brother would put
  his name into every copy of the application, which is the worst shape this project's privacy rule
  can take.
- **Saved:** `%AppData%/TrestleBoard/phrases.json` will hold real names the moment somebody keeps
  "the words we used for Brother Smith". `phrases.json` is gitignored, and every fixture in the
  tests is fictional.

## 7. What guards it

- **`tests/Core.Tests/PhraseLibraryTests.cs`** (9): the five paragraphs are there; none names
  anybody; every token in the text is a question somebody is asked and every question ends in a
  question mark; an unanswered blank prints as underscores; whitespace-only counts as unanswered;
  answers are trimmed; each carries a when-to-use line and enough text to be a starting point; no
  shipped paragraph speaks like a typesetter; a user's own paragraph has no blanks.
- **`tests/App.HeadlessTests/PhraseShellTests.cs`** (9): the words land where the cursor is; one
  undo takes the whole paragraph back out; typing afterwards and undoing does **not**; both commands
  refuse in words with the right instruction; the shelf survives a restart, keeps the shipped ones
  first, replaces rather than twins, and shrugs off a corrupt file; the window asks one blank at a
  time and reads back before anything goes in; §6 on every button.

### Failure-first evidence

Making an unanswered blank print its token failed `ABlankLeftEmptyReadsAsABlankRatherThanAsAFault`
and `WhitespaceOnlyAnswersCountAsUnanswered`; executing `InsertBlock`'s children loose instead of as
a composite failed `TakingBackWhatYouTypedAfterwardsDoesNotTakeBackTheWords`. Three named failures,
and only those.

## 8. What was NOT done

- **No date picker.** There is none in the app, and the nearest precedent — the event card's "When"
  field — is deliberately free text with an example, because "Saturday, 6:00 pm" and "the first
  Tuesday" are both real answers. The blank asks in the same spirit.
- **No categories, no search, no reordering.** Five paragraphs plus a handful of the user's own is a
  list you read, not a list you navigate.
- **No editing a saved paragraph in place.** Saving under the same name replaces it, which is the
  same act with one fewer command to explain.
- **The wizard machinery was not reused.** `WizardSession` requires an `IWidgetDefinition` — a
  widget type id, a default size and a layouter — and inventing a fake widget to ask two questions
  would leave a phantom widget in the type system for the sake of a shape the window already has
  from M51 and M52.

Suite after M54: **1334 passing, 12 skipped** (18 new). No baseline moved, no screenshot re-baked.
