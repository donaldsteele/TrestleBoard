# M54 — Words for hard news

**Delivered 2026-08-08.** PLAN.md §11 M54.

> **✔ The owner signed the wording off on 2026-08-09.** PLAN.md's acceptance said the tone of the
> shipped paragraphs is reviewed by the owner before shipping, because *this is lodge voice, not app
> voice*. All three of §3's open choices are now decided: the memorial register was rewritten, the
> Almoner became the **Secretary** *and* a setting, and the memorial's claim of long service stands
> as drafted. §3 keeps the drafts and the reasoning beside the rulings.

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

**One blank is never asked about.** From 2026-08-09 the office in the sickness paragraph comes
pre-filled from the user's settings and appears on the read-back screen, changeable for that one
insert. A blank the app already has an answer for is not a question, and the moment this wizard is
opened is the wrong moment to ask one nobody's answer changes from month to month. §3 choice 2.

**A blank may be left empty**, and that is deliberate: a memorial is often written before the date
is settled. What prints is then a line of underscores — something a person can see and fill in —
rather than `{date}`, which reads as the program having gone wrong. Two tests hold that.

**What arrives is ordinary editable writing.** The paragraph lands in the newsletter as words the
committee can change, and most of them will. It is a starting point, not a form letter.

## 3. The wording, as the owner settled it (2026-08-09)

Each paragraph below is what a committee member will read. `{name}` and `{date}` are asked for one
at a time. `{office}` is **not asked**: it arrives pre-filled from the user's settings and can be
changed on the read-back screen — see the ruling on choice 2.

- **A memorial notice** — *"It is with sorrow that we record that on {date}, Brother {name} laid
  down his working tools and joined that Celestial Lodge above, where the Supreme Architect
  presides. He gave many years of faithful service to this lodge and to the craft, and his place
  among us will not easily be filled. The brethren extend their heartfelt sympathy to his family,
  and he will be remembered with affection whenever we meet."*
- **Sickness and distress** — *"Brother {name} is unwell at present, and the lodge holds him in its
  thoughts. Cards and visits would be welcome. If you would like to know how best to help, please
  speak to the {office}, who is keeping in touch with the family."*
- **A get-well message** — *"The brethren send their warmest wishes to Brother {name} for a full and
  swift recovery. We look forward to welcoming him back to lodge before long."*
- **Welcome to a newly raised brother** — *"The lodge is pleased to welcome Brother {name}, who was
  raised to the Sublime Degree of Master Mason on {date}. We congratulate him on his progress
  through the degrees, and we look forward to his company at our meetings for many years to come."*
- **Thank you to a degree team** — *"The Worshipful Master extends the thanks of the lodge to the
  degree team who worked on {date}. Their preparation and their care did credit to the craft, and
  the evening will be remembered by all who were present."*

Three choices in there were the owner's to confirm, not mine. All three were put to him and all
three came back on **2026-08-09**. The drafts and the reasoning stay below, because the next person
to reword one of these paragraphs needs to know what was already considered and rejected.

1. **"called to the Celestial Lodge above"** rather than "passed away" or "died" — *drafted as:*
   "It is with sorrow that we record the passing of Brother {name}, who was called to the Celestial
   Lodge above on {date}." PLAN.md itself uses the phrase, and it is the one a lodge would print —
   but it is a jurisdictional usage and the owner had to say whether it is this lodge's.

   **Ruled 2026-08-09: rewritten in the owner's own register** — "on {date}, Brother {name} laid
   down his working tools and joined that Celestial Lodge above, where the Supreme Architect
   presides". The date leads the sentence because the longer phrasing pushes it too far from the
   verb otherwise. The text above is what ships.

2. **The Almoner is named** in the sickness entry as the person to speak to — *drafted as:* "please
   speak to the Almoner, who is keeping in touch with the family." The worry recorded here was that
   a lodge routing this through its Secretary would be given a wrong sentence.

   **Ruled 2026-08-09: it is the Secretary at Indian Land 414, and the office is a setting.** Not a
   correction of one word — the owner's point is that the office varies by lodge and can change year
   to year, so it must not be baked into the text at all. Three things follow, and they are the
   whole of this change:

   - **It is `{office}`, a blank, filled through the same `Phrase.Fill(answers)` seam as `{name}`
     and `{date}`.** `TrestleBoard.Core` references BCL only (§9); it cannot read `AppSettings` and
     does not learn how. `TrestleBoard.App` hands the value in
     (`MainWindow.WhatTheAppAlreadyKnows`).
   - **It is pre-filled, never asked.** M54's design is that blanks are asked one at a time at an
     emotionally hard moment; a "who handles this?" screen on every sickness notice would make the
     feature worse for the sake of an answer that is the same every month. `PhraseBlank` gained a
     `Default`, and a blank that has one is not a question: the wizard skips it in the one-per-screen
     run and shows it on the read-back screen as a filled-in box the user may change **for this one
     insert**, with the sentence above it rewriting itself as they type.
   - **Free text, not a list of offices.** A fixed list renders more safely and is wrong for the
     first lodge whose answer nobody here thought of — Chaplain, Junior Warden, a sunshine
     committee, a named visiting officer. The two ways free text usually goes wrong are both closed
     here: it is one short phrase in one sentence, and the committee reads that sentence back before
     it goes in. **Empty is the only value it may not have**, so `AppSettings.Normalised()` puts
     "Secretary" back, and `Phrase.Fill` falls back to the blank's own `Default` before it falls back
     to underscores. "Please speak to the , who is keeping in touch" cannot be produced from either
     end, and four tests hold it.

   The setting lives in `AppSettings.SicknessContactOffice` and is asked for in "How things look" as
   *"Who should members speak to about sickness and distress?"* — the question, not the name of a
   key. Its answer joins the preview sentence that dialog already announces as a polite live region
   (M70 (f)), so a screen-reader user hears what will be printed before pressing Save.

3. **The memorial claims "many years of faithful service"**, which will occasionally be untrue of a
   brother who was raised recently. The alternative is a blander sentence that says less for
   everyone; the committee can edit either way, and a starting text that says something is the point.

   **Ruled 2026-08-09: kept as drafted.** The owner rewrote the sentence before it in the same
   sitting (choice 1) and left this one standing. Nothing in the shipped text changed for it.

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

- **`tests/Core.Tests/PhraseLibraryTests.cs`** (14): the five paragraphs are there; none names
  anybody; every token in the text is a question somebody is asked and every question ends in a
  question mark; an unanswered blank prints as underscores; whitespace-only counts as unanswered;
  answers are trimmed; each carries a when-to-use line and enough text to be a starting point; no
  shipped paragraph speaks like a typesetter; a user's own paragraph has no blanks. **From
  2026-08-09:** the sickness words say Secretary with nothing supplied; a lodge that says Chaplain
  gets Chaplain; an empty or whitespace office falls back to the default rather than leaving "the ,"
  or a row of underscores mid-sentence; and `{office}` is the only blank that arrives already
  answered.
- **`tests/App.HeadlessTests/SettingsTests.cs`**, **from 2026-08-09**: the office starts at
  Secretary and survives the disk; emptied, it normalises back; the dialog asks the question in
  words on screen and to a screen reader, previews the sentence that will be printed, and hands the
  office back on Save without resetting what it does not show.
- **`tests/App.HeadlessTests/PhraseShellTests.cs`** (12): the words land where the cursor is; one
  undo takes the whole paragraph back out; typing afterwards and undoing does **not**; both commands
  refuse in words with the right instruction; the shelf survives a restart, keeps the shipped ones
  first, replaces rather than twins, and shrugs off a corrupt file; the window asks one blank at a
  time and reads back before anything goes in; §6 on every button. **From 2026-08-09:** the sickness
  paragraph has two blanks and asks one question, with the office already filled in and editable on
  the read-back screen; and the setting reaches the words that actually go into the newsletter.

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
