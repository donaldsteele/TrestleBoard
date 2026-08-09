# M63 — How do I…?

**Delivered 2026-08-09.** PLAN.md §11 M63.

## 1. What it is for

"Show me an example newsletter" has existed since M10; a way to ask the app how to *do* something
has not. This audience will not read a README on GitHub, will not find a wiki, and will not open a
browser to look for one. The help has to live where the confusion does — inside the app, on the key
they already know, answering the words they already use.

Two deliverables: a **"How do I…?" window** reached from `F1`, and a **five-screen first-run tour**
that teaches the shape of a month rather than the buttons.

## 2. The help is generated, not written

`TrestleBoard.Editing/Help/HelpIndex` builds one `HelpTopic` per `ActionCatalog` entry, in the
catalog's own declaration order — which is menu order, so an empty search box browses the app the
way the menus present it.

**This is the whole architectural claim of the milestone.** Hand-written help is a promise nobody
can keep: a command gets renamed, a shortcut moves, and the help goes on confidently describing the
app of eighteen months ago. Nothing catches it, because nothing can. Generating every answer from
the catalog makes the drift structurally impossible — there is nowhere for a stale sentence to
live. The catalog was already carrying the title, the plain-language description, the group and the
shortcut for the menu bar, the action panel, the keyboard table and the tests; M63 adds a sixth
reader and no new source of truth.

`EveryCommandThisAppHasCanBeAskedAbout` asserts topic ids equal catalog ids exactly. A new command
with no help topic is not possible: adding the catalog entry *is* adding the topic. This is the M11
discipline applied one layer further out.

The index is pure — no Avalonia, no window, no file — so the searching is tested exhaustively
without a headless session, the same split M51's `ReviewChecklist` and M58's `ReadAloudSession`
use. The window in the App layer is a renderer over it.

## 3. The vocabulary gap

Generation gives every command its own name for free. It does not help the person who does not know
the app's word for the thing. The app says "photo" and a seventy-year-old types "picture"; the app
says "Make the PDF" and they type "print"; the app says "address book" and they type "members".

`HelpSearchWords` is a small table for exactly that gap and nothing else — about thirty actions,
deliberately partial. Most titles already contain the word anybody would look for. What actually
proves the search works is the corpus test: `TheWordSomebodyWouldTypeFindsTheThingTheyMeant` is
twenty-odd rows of a real word a real user would type against the command they meant, asserted to
rank **first**.

Two findings came out of writing that corpus:

- **"Too small" and "shrink" must not overlap.** The two complaints are mirror images — "the writing
  is too small" wants Bigger, "it does not fit" wants Smaller. A word shared between them lets the
  wrong one win by declaration order.
- **"Mistake" is genuinely ambiguous and was left ambiguous.** "Show my spelling mistakes" is called
  that, and "Look it over with me" is about finding mistakes too. The corpus tests `oops` for Undo
  instead. "Mistake" remains a search word for Undo and finds it third, behind the two commands
  literally about mistakes — which is the right order, and forcing Undo to the top would have been
  teaching the box that one reading of an ambiguous word is the only one.

`EverySynonymNamesARealAction` guards the table the way
`KeyboardAuditTests.EveryRegisteredGestureNamesARealAction` guards the keyboard table: a renamed
action otherwise leaves dead search words behind, and dead search words fail *silently* — the user
simply does not find the thing.

## 4. How the searching ranks

Prefix match on whole words, never substring: "align" finds "Aligns" and "photo" finds
"photograph", but "ear" does not find "Year". A substring match on short words turns a search box
into a random-answer machine.

**Every** typed word must match something — a second word narrows rather than widens. Somebody who
types "picture caption" means both, and answering with every photo command *and* every caption
command buries the one thing they asked for.

Scores, and their ordering is the entire ranking policy: the command's own title (8) beats what a
confused user might call it (6), beats the sentence describing it (3), beats the part of the app it
lives in (2). The whole query written out as a command's exact name adds 100, so somebody who typed
"bold" gets Bold and not every command that mentions bold text.

## 5. The menu path is read off the menu bar

An answer that says where to find a command is worth having, and typing `"File ▸ Make the PDF"` into
a topic would be wrong within a month — the menus have already been reorganised twice (M11, M17).

`TrestleBoard.App/Help/MenuPaths` walks the live menu bar instead. Every `MenuItem` already carries
its action id in `Tag` (again the M11 discipline), so the menu bar can simply be asked, once per
window, for a few milliseconds. Access-key markup is stripped by hand rather than with
`Replace("_", "")`, which would eat the escaped `__`; the ellipsis is removed wherever it falls,
because "How do I…?" ends with a question mark and trimming from the right would have left that one
path reading differently from all the others.

This is why the menu path lives in App and not in `HelpTopic`: it is a fact about XAML, and the
Editing layer cannot see XAML. `TheMenuPathIsLeftForTheAppLayerToFillIn` pins that boundary.

## 6. "Take me there", and the refusal that explains itself

Where the command can run right now, the answer offers **Take me there**, which runs it. Where it
cannot, the button is *absent* and the reason appears instead — in the same words the menu bar and
the action panel would use, because `ActionCatalog.Evaluate` is the one thing that answers "can I,
and if not, why not". M11's rule holds here too: silence would read as "the help is broken".

The window is **not modal**, and stays open behind the command it launched. Help that locks the app
while you read it is help you have to memorise first, and "Take me there" would be a lie if you were
not allowed to touch what it took you to. Somebody who asked how to do one thing is very often about
to ask how to do the next; making them find the window again each time is the small cruelty that
stops people using help at all.

`HowDoI` and `ShowTheTour` are declared **always available**. An app that will not tell you how to
do something because of what you have selected is refusing at the exact moment help is needed.
`AskingForHelpIsNeverRefused` holds it.

## 7. The tour teaches the month, not the buttons

Five screens: welcome; start from last month; let the tables fill themselves in; look it over before
it goes; make the PDF, then send it. Written in the second person and in the committee's own words —
"the brethren", "the trestle board" — because it is the first thing the app ever says.

Somebody meeting this app does not need to be told what Bold does. They need to know that a
newsletter starts from last month's, that the tables fill themselves in, that the app will read it
back before it goes, and that the PDF is the thing they send.
`TheTourWalksTheMonthRatherThanListingFeatures` asserts each of those five beats is actually
mentioned. Screen five ends by naming "How do I…?" — a tour whose last word is "good luck" has
taught nobody where to ask next.

A plain `int` screen counter, not `WizardSession`, for the reason `ReviewWindow`,
`RosterImportWindow` and `PhraseWindow` each gave: that type is bound to `IWidgetDefinition` and
exists to collect and commit data. A tour collects nothing.

## 8. Shown once — and skipping counts as seeing it

`AppSettings.HasSeenTheTour` is written **the moment the tour opens**, not when it finishes.
Somebody who closed it on screen two has decided; asking again next Tuesday would be overriding that
decision. `Help ▸ Show me round again` always reopens it, or that menu item would be one that
silently did nothing.

The decision is factored out as `MainWindow.ClaimTheTour(becauseTheyAsked)` — deciding *and*
recording in one step — so the once-only rule is testable without driving a modal dialog. A test
that had to walk the window would be testing the window.

**The flag defaults to false, so an existing committee sees the tour once after updating.** That is
deliberate and is the safer of the two wrong answers: defaulting to true would hide it from everyone
who already has TrestleBoard, including the successor who inherited the laptop and has never seen
the app before — the person the tour is most for. One skippable window, once, is the cost.

Not hung off Velopack's `OnFirstRun`: that is an installer hook that runs and exits before Avalonia
is up, so a tour behind it would run with no window and never be seen again. It runs at the top of
`RunStartupAsync`, before the recovery offer and the start screen, and modally — everything else
there assumes the person already knows what the app is for, and a non-modal tour at start-up would
open underneath the start screen.

## 9. Accessibility (§6)

18pt+ throughout; 20pt buttons at 44px minimum height; the search box at 24pt. `F1` is the one
shortcut this audience knows from every other program they have ever used, and was the only
unmodified function key still free.

- Focus lands **in the search box** on open — no Tab first.
- The result count is a polite live region, so the count changes as they type without interrupting
  them, and "nothing matched" says *what to do next* rather than showing a bare zero.
- The best answer is already open without being clicked: this audience reads the first answer as
  the answer.
- Both windows rename themselves as content changes (`"How do I…? — Insert a picture"`, `"A quick
  look round — Screen 2 of 5"`) and the tour focuses each new heading. Avalonia has no live region
  for a whole panel — this is the `WizardWindow` technique from docs/M7-spec.md §6.6.
- The search hint is real text, not only a watermark: a watermark vanishes the moment somebody
  types, and a screen reader may never have announced it.
- On the first tour screen, Back is **absent**, not dimmed. `Alt+N` and `Alt+B` move between
  screens; `Escape` leaves either window.
- Both windows are in `AccessibilityTests`' walk, and `docs/accessibility-test-script.md` gains
  §16 — eleven manual screen-reader steps and their result rows. That section matters more than its
  length suggests: a screen-reader user who cannot work out how to do something arrives *here*.

## 10. Neither window has an icon

`ActionIcons` records both as deliberately text-only. A question mark is the obvious glyph for help
and it is the wrong one — it is also what this app puts beside anything it is *unsure* of, and the
one place help must not look is uncertain. "How do I…?" is already the clearest possible label. The
tour is reached from the Help menu once a decade and never hunted for.

## 11. What guards it

| Test | Holds |
| --- | --- |
| `EveryCommandThisAppHasCanBeAskedAbout` | topics ≡ catalog ids, plus an anti-vacuity count |
| `NoTopicIsLeftWithNothingToSay` | no topic with a blank title or answer |
| `TheWordSomebodyWouldTypeFindsTheThingTheyMeant` | the real-word corpus, each ranked first |
| `TypingASecondWordNarrowsTheAnswer` | all words must match |
| `AWordIsMatchedFromItsStartAndNotFromTheMiddle` | "ear" does not find "Year" |
| `TheCommandsOwnNameOutranksASentenceMentioningIt` | the exact-title bonus |
| `EverySynonymNamesARealAction`, `NoSynonymIsBlank`, `EveryActionWithSearchWordsHasAtLeastOneTheTitleDoesNotAlreadySay` | the synonym table cannot rot or pad |
| `EveryCommandInTheMenusCanBeToldWhereItLives`, `NoPathCarriesTheAccessKeyMarkup`, `ACommandInsideASubmenuIsNamedThroughIt` | the paths read off the live menu bar |
| `SomethingThatCannotBeDoneRightNowSaysWhyInsteadOfOfferingIt` | reason, not silence |
| `TakeMeThereRunsTheThingTheyAskedAbout` | and the window stays open |
| `AskingForHelpIsNeverRefused` | help is always available |
| `TheHelpWindowOpensAndOnlyOneOfItIsEverOpen`, `EscapeClosesTheHelpWindow` | window lifetime |
| `TheTourIsFiveScreensLong`, `TheTourWalksTheMonthRatherThanListingFeatures`, `TheLastScreenSaysWhereToAskEverythingElse` | the tour teaches the cycle |
| `TheTourIsOfferedOnceAndThenOnlyWhenAskedFor` | shown once; skipping counts |
| `AccessibilityTests` walk | both windows |

Two shots join the screenshot harness — `how-do-i` (with "picture" typed, because an empty box shows
a list and the thing worth showing is that plain words find the answer) and `first-run-tour` (screen
two). `MilestoneGate` names M63 so the harness refuses to run against a build without it.

## 12. Privacy

No personal data of any kind. The help text is generated from the catalog, which contains no names;
the tour's examples are the app's own commands. Both screenshot fixtures are synthetic and neither
window can read the roster. **No network** — the help is bundled, never fetched, which is also what
keeps it working in a lodge basement.

## 13. What was NOT done

- **No context-sensitive help.** `F1` opens the same window everywhere rather than jumping to a
  topic for whatever is selected. Guessing what somebody is confused about and being wrong is worse
  than a search box, and the search box is one keystroke from every answer.
- **No screenshots or animation in the help.** Every answer is words. Pictures in help go stale in
  exactly the way §2 exists to prevent.
- **No searching the newsletter's content from this box.** It answers "how do I", not "where is".
- **No tour replay prompt.** The app never suggests the tour again; the menu item is the only way
  back, because a program that keeps offering to explain itself reads as a program that thinks you
  are struggling.
