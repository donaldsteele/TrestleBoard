# M27 — Say what you mean

**Delivered 2026-08-07.** §14.5 grouping 4, in part. Wording, labels and the one dialog that had no
way out. What is **not** in it is listed in §5, because a partial milestone that says so is worth
more than one that implies it finished.

---

## 1. Raw style ids reached the user

Format ▸ Paragraph style, and the same list in the action panel's flyout, were built with
`Header = styleRef` — so the committee was shown `body`, `heading`, `subheading`, `caption`,
`quote`. `Core.Text.StyleLabels` exists for precisely this ("The committee must never be shown
`body-bold-italic`") and was simply never called from either place. It was the last surface in the
app still speaking the document model's vocabulary at the user.

Both now call `StyleLabels.Describe`, so the menu reads "Body text", "Headings", "Photo captions".

Checked while fixing it: every paragraph role a real document carries (`body`, `heading`,
`subheading`, `caption`, `quote` — `StandardStyles`) has a hand-written label, so the honest
`"Style: {id}"` fallback is unreachable in practice. The roles the review also listed —
`table-row`, `lodge-table` — are *character* styles and never appear in this menu.

## 2. The toolbar and the menu disagreed about what commands are called

| Toolbar said | The command is called | Why it mattered |
| --- | --- | --- |
| Smaller / Bigger | Zoom out / Zoom in | The font window's size stepper *also* says Smaller/Bigger, where it changes point size |
| Back / Next | Previous page / Next page | The officers wizard's own buttons say Back/Next |

The same word doing two jobs in one program is how a user learns not to trust any of them. The
toolbar now uses the catalog's words, and a test holds it there.

## 3. The adjust window had no way back

`PhotoAdjustWindow`'s sliders apply to the document as they move — that is the point of it, the user
watches the page change — and the window had only a "Done" button. No Cancel, no `IsCancel`, so Esc
did nothing at all. For an app whose users learn what a slider does *by moving it*, a window with no
way out is the wrong shape.

"Start over" was the nearest thing and is a different promise: it resets the picture to the original
file, discarding adjustments made before this window ever opened.

There is now a **Cancel — change nothing**, which unwinds the real undo stack back to where it stood
when the window opened. `DocumentSession.UndoDepth` and `PhotoController.UndoBackTo` are new and
exist for this. Unwinding the actual stack rather than restoring a remembered recipe is deliberate:
the sliders commit through the same commands as everything else, so the stack already *is* the
record of what the window did, and a snapshot would be a second notion of "before" that could
disagree with it.

## 4. Smaller wording repairs

- **"Turn a quarter"** did not say which way. It is now **"Turn it right ↻"**, with the direction
  in its screen-reader name too. Four presses to undo a wrong guess is four undo steps.
- **The position dialog contradicted itself.** It described a *"blue crop window"* over a rectangle
  drawn black, and promised the crop "stays the same size" directly beside two Zoom buttons that
  look like the thing that would change it. Both statements were true and read as contradictions.
  It now names what is on screen ("the frame outline"), says what it is choosing ("which part of
  the picture shows"), and the zoom buttons became **"Show it larger" / "Show it smaller"** with a
  sentence saying they change only the preview.
- **The 16pt floor** (PLAN.md §6) was broken in five places: the canvas hint that teaches a
  first-time user what an empty frame is for (14pt — the smallest text in the app, where it mattered
  most), the action panel's refusal-reason and "what's next" text, several 15pt labels in the font
  window, and the licence text. All raised to 16.

---

## 5. In grouping 4 and NOT done

Recorded so the next session does not have to re-derive which half was finished.

- **The grey-means-two-things problem** (§14.4's first and largest visual item). Available secondary
  buttons and genuinely disabled ones share Fluent's grey slab, so the start-screen cards and the
  action-panel items read as disabled. Fixing it properly means a palette token pair and a
  `ControlTheme` that every secondary button opts into — and `Controls.axaml` documents why a bare
  `Style Selector="Button"` is not available (it would deform ComboBox toggles and ScrollBar repeat
  buttons, which are Buttons too). That is a look-and-feel decision across thirteen dialogs and is
  the owner's to make, not one to slip into a wording milestone.
- **Selection chrome ignoring High Contrast** (`FrameOverlayRenderer`'s hard-coded ARGB). Needs the
  palette to reach the Skia renderer, which nothing does yet.
- **Consolidating the four near-synonym picture verbs** ("Fix", "Adjust", "Trim", "Position"). A
  real information-architecture change, not a rename.
- The rest of the §14.3 jargon inventory: "text frame", "Wrap text around this", "Bring forward",
  "This item", "A shape is selected".

---

## 6. What guards it

`PlainLanguageTests`, new:

- `TheParagraphStyleMenuNamesRolesInPlainLanguage` — no lower-case hyphenated id and no
  `"Style: "` fallback reaches the menu, and the declared labels are there.
- `EveryToolbarButtonSaysWhatTheCatalogCallsThatCommand` — compares against `ActionCatalog`, which
  is the one place a command is named (M11) and what the menu bar already reads from. A shorter
  label is allowed; a different one is not. `MainWindow.ToolbarButtons` was extracted from
  `RefreshActions` so the wording walk and the availability walk cover exactly the same buttons.
- `NoChromeTextIsSmallerThanSixteenPoints` — **scope stated in the test itself**: it walks the main
  window's TextBlocks and Buttons. It cannot see Skia-painted canvas text or closed dialogs, so the
  canvas hint is guarded by a comment at its call site and nothing more.

The first two were run against the unfixed code and confirmed to fail. The third was verified to
catch a real regression (a chrome label dropped to 13pt).

Suite after M27: **1163 passing, 12 skipped**. No snapshot baseline moved.
