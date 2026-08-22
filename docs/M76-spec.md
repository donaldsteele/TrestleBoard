# M76 — the chrome stops looking like 1998

**Delivered 2026-08-21.** Written the same day, ahead of the work rather than after it — §1–§10 are
the specification as it was approved, and §11 is what happened when it met the application.
PLAN.md §11 M76
is the summary; this document is the whole of the reasoning, and it is written first because two of
the three lanes below change §6 surfaces and one of them adds a command — neither is a repaint that
can be justified in a commit message.

> **The palette is not the problem and is not being replaced.** M16 measured every pair in this
> application and wrote the ratios into the file the test reads. Nothing in that block is deleted
> here. The finding of this milestone is that a correct palette applied at exactly one elevation,
> with the platform's default control fills underneath it, produces a window that is *accessible and
> dated at the same time* — and that those are separable.

## 1. What was examined

The eight screenshots under `docs/images/` that frame the shell — `hero-issue-page1`,
`action-panel-whats-next`, `action-panel-photo`, `start-screen`, `settings`, `people-window`,
`scale-200`, `high-contrast` — read against `Theme/Palette.axaml`, `Theme/Controls.axaml`,
`Theme/Tokens.cs`, `MainWindow.axaml` and `Actions/ActionPanel.cs`.

Seven findings. Three are visual, three are workflow, one is a defect that ships in the default
window size and has been visible in a committed screenshot since M69.

## 2. The defect first: the toolbar overflows at the window's own default width

`MainWindow.axaml` opens at `Width="1280"`. At that width `hero-issue-page1.png` shows "Zoom in"
clipped by the window edge and "Fit page" not on screen at all, with a horizontal scroll track
running the width of the bar underneath it.

**This is M69 working exactly as designed, on a case M69 was not looking at.** That milestone made
the toolbar's scrollbar permanent so that a control pushed out of reach would announce itself
instead of being silently cut off — and it was right, and the screenshot it regenerated is the proof
it works. What nobody asked was *why the toolbar was overflowing at the default size in the first
place*, because M69's report was about 200% scale, where overflow is expected and correct.

At 100%, on the size the application chooses for itself, a scrollbar on a toolbar is not a rescue.
It is the first thing a new user sees, and it says the program does not fit in its own window.

**The fix is not a wider window and not a smaller font** — §6 forbids the second and the first only
moves the number at which it happens. It is that four of the twelve controls on that bar do not
belong to the file. Zoom in, zoom out, the zoom percentage and Fit page act on the *view of the
page*, not on the newsletter; they belong to the canvas and are moved to a footer strip inside it,
bottom-right, where every document application this audience has ever used puts them. The toolbar
then holds Open, Save, the save state, Undo, Redo, Previous page, the page label, Next page — and
has room for the one control §3 argues has been missing.

## 3. Emphasis inflation, and the button that is not on the toolbar

`action-panel-photo.png` shows three navy-and-gold primaries stacked in one panel; the what's-next
card shows two. M16 defined that treatment as "the offer you probably came for", singular. Three
simultaneous offers is not three answers to that question, it is the absence of an answer, and the
gold bar — the most expensive ornament in the palette, and the only place the lodge's own gold is
legal — is spent on all of them.

**Meanwhile the actual terminal goal of the product is not on the toolbar.** "Make the PDF" is what
the committee is here to do; §7 calls the PDF the file they email to the lodge. Today it appears
only inside the what's-next card, which scrolls, and only when the card decides to offer it.

So: at most one primary per panel group, chosen by rank rather than by a boolean each action sets
for itself; and `newsletter.exportPdf` takes a permanent, right-aligned place on the toolbar, in the
primary treatment, which is now unambiguous because it is the only one in that region.

## 4. One elevation, and the platform's grey underneath it

Menu bar, toolbar, action panel, status bar and every dialog body all paint `Chrome.Background`.
Buttons paint whatever Fluent gives them, which M37 measured at `#BEBFC2`. The result is a window
with no figure and no ground: the borders M37 added are doing all the structural work alone, and a
border is a thin thing to ask that of.

**The answer is one new token, not a new palette.** `Chrome.Surface` is the raised plane — button
fills, list and form cards, the page rail's tiles. `Chrome.Background` keeps every value it has and
every pair already measured against it, and becomes the recessed ground those surfaces sit on.

```
PAIR Chrome.Foreground on Chrome.Surface >= 4.5 : Light 17.79  Dark 13.05  HighContrast 21.00
PAIR Chrome.Muted on Chrome.Surface >= 4.5 : Light 7.54  Dark 6.73  HighContrast 21.00
PAIR Chrome.Border on Chrome.Surface >= 3 : Light 4.34  Dark 4.70  HighContrast 21.00
PAIR Accent on Chrome.Surface >= 3 : Light 12.48  Dark 6.75  HighContrast 16.75
PAIR Focus on Chrome.Surface >= 3 : Light 17.79  Dark 13.05  HighContrast 19.56
PAIR Warning on Chrome.Surface >= 4.5 : Light 7.85  Dark 7.28  HighContrast 10.64
PAIR Chrome.Surface on Chrome.Background >= decorative : Light 1.15  Dark 1.15  HighContrast 1.00
```

Values: Light `#FFFFFF`, Dark `#262A33`, High Contrast `#000000`. Every floor clears, and the two
that matter most improve on what they replace — `Chrome.Foreground` on the Fluent grey was 9.68:1
and is 17.79:1 on white.

**The last line is the one a reviewer will challenge, so it is argued here.** Surface against
Background is 1.15:1 and is declared `decorative`, which is the same declaration `Chrome.Divider`
already carries. That is legitimate for exactly one reason: **the tone is never the boundary.** A
button is bounded by the M37 border, a card by `Rule` on the side that meets the ground, the panel
by the rule it already has. The tonal step is reinforcement, and the palette's own rule — colour is
never the only signal — is satisfied because colour is not *any* of the signal here, only comfort.
In High Contrast the two collapse to the same black and nothing is lost, because there the border
steps to 2px and does the whole job on purpose.

Two shape tokens join it. `Radius.Control` (6) and `Radius.Surface` (10) exist so that a corner
radius is a decision made once rather than a number typed into forty controls; and
`Elevation.Sheet`, a single soft shadow, spent only on the page. The sheet is the one thing in the
window that is pretending to be a physical object, and a shadow is how paper says so. High Contrast
sets it to none — the 21:1 step between sheet and backdrop there is already unmistakable, and a
blur under a high-contrast user's page is noise.

## 5. The chrome has no typeface

`assets-src/fonts/` carries twenty-two licensed families with SHA-256 entries in `fonts.json`, all
shipped, and the application's own chrome uses whatever the OS hands it — which is one more thing
that differs across the three platforms in a program whose central guarantee is that it does not.

Chrome and dialogs take **Source Sans 3**: already bundled, already licensed, humanist, open
apertures, and legible at 16pt to eyes §6 exists for. The start screen's wordmark — and nothing else
in the application — takes **Cinzel**, which is engraved Roman lapidary lettering, which is what the
title of a printed trestle board has always been set in. Two occurrences in the whole product.

**This deliverable carries a spike and may be cut on its result.** The bundled faces are loaded for
*layout*, through SkiaSharp and HarfBuzz; whether Avalonia can resolve the same files as an
`avares://` `FontFamily` without a second copy on disk has not been established. If it needs a
second copy, the deliverable is cut: a duplicated TTF is a licence-tracking hazard and `fonts.json`
is the single register of what this program ships. Determinism of *layout* is unaffected either way
— chrome text has never been laid out by the document engine.

## 6. Navigation: five pages, and one way to see the fourth

`Page 1 of 5` sits between two buttons, and pressing Next three times is the only route to page 4.
A four-to-six page newsletter is small enough to show whole, and showing it whole is what a trestle
board *is* — the plan laid out where everyone can see it.

**The page rail** docks left: one miniature sheet per page, in order, current one marked by an
accent bar with the gold notch (gold on the accent, which is the only ground its key name permits).
Tiles are `Chrome.Surface`. It is collapsible to a labelled button exactly as the action panel is,
and it folds itself away below the same window width, because the page must never be squeezed out.

**Reordering is on the rail and does not require a drag.** `page.moveEarlier` and `page.moveLater`
have existed in `ActionId` since the page commands landed; the rail gives them a place where the
thing they move is visible while they move it. Drag is an accelerator, never the only path (§6).

**One new command, and one design question it raises.** `page.goTo` is a command with a *parameter*
— which page — and the catalog has no precedent for that: every entry answers "can I, and if not,
why not" for a command that takes nothing. Two candidate answers, and the first is recommended:
carry the target in the button's `Tag` beside the `ActionId`, exactly as the menu already carries
its id, and let `ActionRunner` read it — the catalog entry then means "can you jump to a page at
all", which is a true and useful thing to be able to refuse ("There is only one page."). The
alternative, an id per page, is rejected: it makes the catalog's size depend on the document.

`view.togglePageRail` joins `view.toggleActionPanel`, with the same shape and the same tests.

## 7. Two smaller things the screenshots show

- **The start screen's template picker is orphaned.** `start-screen.png` puts the Template combo
  below all three tiles, outside the one it belongs to. It moves inside "Start from a template".
- **The start screen has no recent newsletters.** `PastIssues` already knows where they are. This
  audience reopens the same file for a week; making them walk a file dialog each time is the kind of
  cost §6 exists to remove.
- **The zoom percentage is dead text.** `ZoomLadder` exists. The label becomes a button that opens
  it, in the canvas footer §2 moves it to.

## 8. Deliverables

**(a) `Chrome.Surface`, `Radius.Control`, `Radius.Surface`, `Elevation.Sheet`** in
`Theme/Palette.axaml` with constants in `Tokens.cs`, the seven PAIR lines of §4 added to the
contrast block, and the palette header extended with the `decorative` argument made above.

**(b) The control themes take them.** `TrestleBoard.ActionButton` and `TrestleBoard.PrimaryButton`
get `Chrome.Surface` fill (primary keeps the accent) and `Radius.Control`; the disabled rule is
untouched, because M37 settled it and it is still right. The page sheet takes `Elevation.Sheet`.

> **Amended after review.** "The disabled rule is untouched" was true of `ActionButton` and hid the
> fact that `PrimaryButton` never had one. That was harmless while the primary treatment was worn
> only by dialog default buttons, none of which is ever disabled — and (e) below ends it, because
> `newsletter.exportPdf` is refused with no newsletter open and refused again until the issue date
> is chosen, which is the state the application starts in. Without a rule of its own the toolbar's
> gold-and-navy button fell through to Fluent's disabled treatment: measured here at `#66000000` on
> `#33000000` in Light, which is the unreadable pair M37 removed from the action button. The primary
> now carries the same `:disabled` rule the action button has — no border, no fill, `Chrome.Muted`
> label — and `ThemeCompositionTests` asserts it for BOTH themes in all three variants, which
> `EveryPieceOfTextTheWalkCanSeeMeetsItsFloorInAllThreeVariants` cannot, because that walk skips
> disabled controls by design.

**(c) Source Sans 3 for chrome, Cinzel for the start-screen wordmark** — subject to the §5 spike.

**(d) The zoom cluster leaves the toolbar** for a canvas footer, taking `view.zoomIn`,
`view.zoomOut`, `view.fitPage` and the percentage with it. No new `ActionId`; the four keep their
menu items, their shortcuts and their `KeyboardMap` rows unchanged.

**(e) `newsletter.exportPdf` joins the toolbar**, right-aligned, primary treatment.

**(f) One primary per panel group.** `EditorAction.IsPrimary` becomes a rank the group resolves
rather than a flag each action asserts.

**(g) The page rail**, with `page.goTo` and `view.togglePageRail`: catalog entries, availability
rules with written refusals, `KeyboardMap` rows, `ActionRunner` handlers, thumbnails off
`DocumentRenderSource`, and an `AutomationPeer` that names each tile "Page 3 of 5".

> **Amended after review: a miniature is keyed by the page's id, never by its position.** The first
> implementation cached by ordinal, which is stable only until somebody presses the two buttons the
> rail exists to give a home to. A move leaves the page COUNT alone, so the tiles are not rebuilt and
> each one is handed the bitmap filed under its index — the picture of the page that used to be
> standing there — while the label and the accessible name beside it are recomputed correctly, so
> the picture and the name disagree. Add and delete shifted every miniature at or past the edit.
> Keyed by identity the class of bug is gone rather than patched, and a deleted page's bitmap is
> disposed because its id stopped being in the newsletter.

**(h) The start screen's picker moves inside its tile; recent newsletters appear beside it.**

> **Amended after review: and the shell opens what was pressed.** `StartChoice.RecentFile` reached
> the window and neither switch over a start-screen answer had an arm for it, so pressing a recent
> newsletter closed the screen and opened nothing, in silence. The two switches are now one method —
> `MainWindow.ActOnWhatTheStartScreenSaidAsync` — because a second switch is a second thing to
> forget, and `StartScreenTests` drives the shell and asks the WINDOW whether a newsletter came up
> rather than asking the dialog what it decided.

**(i) Screenshots regenerate.**

> **Corrected before the work: "every one that frames the shell" was the wrong scope, and it was
> wrong in this document from the day it was written.** (a) and (b) change the fill and the corner of
> every button the application makes, and a dialog is full of them — so `people-window`, `settings`,
> `font-picker`, `fix-photo`, `wizard-review` and the rest are all repainted by this milestone whether
> or not they show the shell. The honest scope is **every shot of kind `Window` or `Dialog`**, which
> is twenty-six of the twenty-seven in `ShotList`. The exception is `pdf-page-spread`, whose kind is
> `Render`: it is document pixels with no chrome in the frame, and M76 does not touch the document.
>
> Regenerate by name with `--only`, per the churn control `tools/TrestleBoard.Screenshots/Program.cs`
> documents: PNG encoder output is not stable across SkiaSharp versions, so a blanket re-bake would
> rewrite the one image this milestone has no business touching.

## 9. Acceptance

- **The default window shows every toolbar control with no horizontal scrollbar.** A headless test
  at `1280×860` asserts the toolbar's desired width is under the window's, and asserts the four
  moved controls are present in the canvas footer — the second half matters, because deleting them
  would also pass the first.
- **The contrast block is the test.** `ThemeCompositionTests` recomputes all twenty-one pairs from
  the hex values; a wrong number in §4 fails the build.
- **No button is disabled-looking by accident.** `ActionSurfaceTests` already walks every window
  demanding each app-made button carry the action or primary treatment. It must stay green with the
  new fills, and it is the reason (b) changes the themes rather than the call sites.
- **At most one primary per panel group**, asserted over every context the catalog can produce, not
  over a sampled few.
- **The rail refuses in words.** With one page, `page.moveEarlier`, `page.moveLater` and `page.goTo`
  are all unavailable, each with a reason `ActionAvailability` accepted — it refuses an empty one at
  construction, so this is enforced already and the test proves the reason is *true*, not merely
  present.
- **The rail is keyboard-complete**: Tab reaches it, arrows walk the tiles, Enter goes to the page,
  and `KeyboardMapTests` fails on a shortcut without a row.
- **High Contrast is checked by eye as well as by test**, per `docs/accessibility-test-script.md`,
  because §4 deliberately collapses a tonal step there and the argument that nothing is lost is an
  argument, not a measurement.

## 10. What this milestone does NOT do — stated rather than implied

- **No change to any measured pair that already exists.** `Chrome.Background` keeps its three
  values. This is the whole reason `Chrome.Surface` was added above the ground rather than the
  ground being darkened beneath the surfaces: the second would have re-measured fourteen pairs to
  achieve the identical visual step.
- **No change to the document, the layout engine, or any rendering baseline.** Nothing here touches
  what the PDF contains. No snapshot moves. If one does, that is a bug in this milestone.
- **No new font files.** (c) uses what `fonts.json` already registers or is cut.
- **No icon-only control anywhere**, including the rail's move buttons and the canvas footer.
- **No drag-only path.** The rail's reorder has buttons first; drag, if it lands at all, lands after.
- **The menu bar keeps every command it has**, including the four that leave the toolbar. §6's
  "every command has a menu item" is not weakened by moving where a command's *second* home is.
- **`PeopleWindow` and the other dialogs get (a) and (b) and nothing else.** Their layouts have
  their own findings and their own milestone; a visual token pass is not a licence to redesign
  thirteen windows while nobody is looking.

---

## 11. What the work found that the specification did not

Four things, and three of them were found by an image rather than by a test. They are recorded here
rather than folded silently into §8, because the pattern is the one M69 and M75 both wrote down: the
guard existed, and nothing on screen said it had stopped applying.

**The chrome typeface threw rather than degraded.** `UseHeadlessDrawing`'s font manager cannot load
an embedded family, so `Cinzel` raised "Could not create glyphTypeface" out of a *queued render pass
during dispatcher teardown* — which is not a paragraph in the wrong face, it is a window that does
not draw, and it poisoned the session for every test after it. Both declarations now name `$Default`
second. The real application was never affected and that was established before the fix rather than
assumed: the screenshot harness, which draws through real Skia, rendered both faces correctly.

**The rail obeyed M11 and gate 26 called it a defect.** `OfferIntegrityTests` compares every tagged
control's `IsEnabled` against the catalog, and the rail's buttons are pressable-while-refusing by
design — five disagreements, every one of them the rule working. The action panel was already carved
out of that sweep for exactly this reason; the rail joins it, and its integrity is proved instead by
`PageRailTests`, which asserts the stronger thing the sweep cannot: that the refusal text *is* the
catalog's sentence, and that the sentence is true of the state that produced it.

**Both strips folded on the same constant, so at 200% neither folded and the page became a sliver.**
`scale-200.png` showed the rail at 368px and the panel at 720px taking 1088 of a 1280 window between
them, leaving the newsletter about 190. The threshold had compared a constant against the raw window
width since M16, which was exactly right while there was one strip that never changed size; M76 broke
both halves at once. The fold now asks how much of the *page* would be left, at the scale the chrome
is actually drawn at, and the rail's question includes the room the panel has already taken — so the
rail gives way first, because §6 puts the commands in the panel while the rail is a faster route to a
page Previous and Next still reach. **No test caught this**, because every fold test resizes the
window and none of them changes the scale. `AtTwiceTheSizeTheRailGivesWayRatherThanSqueezingThePageOut`
is the one that would have, and it asserts what is left for the page as well as which strips are up.

**And the footer inherited the defect it was built to fix.** Moved off the toolbar to stop four
controls being pushed out of reach, the four then sat in a region narrower than the toolbar had been
— so at 200% "Zoom in" and "Fit page" were cut off at the panel's edge with nothing to say they could
be reached. It now carries M69's own answer, `AllowAutoHide=False`, for M69's own reason.

> **The shape all four share.** Each was a rule that had been correct for as long as the thing it
> governed stood still. The fold threshold was right for one strip, gate 26 was right for one
> never-greyed surface, the footer's placement was right at one scale. M76 moved all three and
> checked none of them against what it had just changed — which is the audit-boundary lesson of
> M69–M74 arriving from the inside for once, in the milestone's own diff.
