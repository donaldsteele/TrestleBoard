# The missing-functionality register — what is left

**Written 2026-09-06**, after M101–M109. This is the tail of the audit done on 2026-09-05 (the
register itself lives in that session's plan file). It exists so the next session picks up from a
list rather than re-deriving one — the audit's own lesson was that repeated "find what is missing"
passes kept missing the same things.

**Read `docs/M91-spec.md` through `docs/M109-spec.md` for what has already been answered, and §A4
below for what must never be re-reported as a gap.**

---

## 1. Built, working, and unreachable — three left

The audit's headline category: the model expresses it, the engine honours it, and no command reaches
it. Fifteen instances have been found and closed. These three remain.

| Capability | Where it lives | Why it is still open |
|---|---|---|
| **Table styling** | `WidgetBlock.TableStyleRef` (`Blocks.cs`), read by `WidgetStyleResolver` | Nothing has ever set it. Cheapest of the three. |
| **Extra page masters** | `Document.PageMasters` is a list; `Page.MasterRef` selects | No command creates, edits or assigns one, so every document has exactly one. Needs a decision about what a second master is *for* before it needs code. |
| **Arbitrary text-frame border and fill colour** | `FrameStyleDef.StrokeArgb` / `StrokeWidthPt` / `FillArgb`; the renderer already draws arbitrary filled and stroked boxes | `PageLooks` collapses it to three fixed styles. **This one is not cheap**: it needs derived FRAME styles, which do not exist — M86's derived styles are CHARACTER styles, and M109's are PARAGRAPH styles. A third grammar, or a reason not to have one. |

## 2. Ordinary verbs still absent from every layer

- **Strikethrough** — the closest thing to shovel-ready in this file. It follows M102's path exactly:
  a fifth dimension on `CharacterStyleResolver`'s variant name, and `PageRenderer.UnderlineRectFor` is
  already extracted as the seam a strike rect would sit beside. ("Cancelled" is the use.)
- **Highlight colour, superscript, subscript, small caps, all-caps.**
- **Hanging indent.** M109 did left and right; the hanging indent in the app is still hardcoded for
  list markers in `TextLayoutEngine`.
- **Smart quotes, autocorrect, auto-capitalise.** Text is inserted verbatim. Behaviour change with
  real risk of annoying the committee — decide before building.
- **Non-breaking space, non-breaking hyphen.**
- **Creating, renaming or deleting a style.** Six paragraph styles ship and that is the permanent set;
  `TextStylesWindow` edits font family and size only.
- **List nesting, promote/demote, multi-level lists, restart numbering.**
- **Widow control and keep-with-next.** Only the orphan half exists
  (`TextLayoutEngine.MinLinesAtBreak`, hardcoded to 2 and unadjustable).
- **Drop caps. Grammar check. Thesaurus.**
- **Page break / column break.** Overflow is answered by frame chaining and `flow.auto` only.

## 3. Layout, page and view

- **Group / ungroup.** No parent, group or children field on `Block`; no `GroupBlock`. Note captions
  already ride with their frame (`ICaptionedBlock`), which is most of what a committee wanted groups
  for — check the need before paying for the model change.
- **Make these the same width / height.** `FrameAlignment` is move-only by design.
- **Circle, arrow, diagonal line.** Shapes are axis-aligned rectangles.
- **Flip, opacity, drop shadow, rounded or dashed borders, per-edge borders** — absent from the model.
- **Crop to a shape / circular picture mask.**
- **Layers or object-list panel.** Z-order is four relative commands; objects cannot be named.
- **Copy a page from another newsletter.** (Duplicate-a-page shipped as M100.)
- **A header.** The footer's *wording* is deliberately fixed — see §A4.
- **Snapping cannot be turned off.** The only escape is holding Alt, which no menu, catalog entry or
  help topic mentions. **The cheapest honest fix is documenting Alt**, not a setting.
- **Nudge amount is a `const`**, and Ctrl+arrow only ever resizes the right and bottom edges.
- **Zoom to selection; a typed zoom percentage.** (Fit-width shipped as M108; the ladder's floor is
  0.5, not 100% — see the correction in §A4.)
- **Full-screen / distraction-free view.**
- **Hyperlinks are auto-detected only** (M78). No way to link chosen words, edit or remove a detected
  link, link a picture, or link to another page. Invisible on screen and absent from the PNG export.
- **A general table.** `insert.simpleList` is capped at three columns, no merged cells, no column
  widths, no typing directly into a cell.
- **Charts.**
- **Single-page canvas only** — no continuous scroll, no spread view. For a newsletter whose pages
  face each other in print this is a real limitation, and it is *why* M91 is commands rather than a
  drag gesture.

## 4. Deliberate — do NOT re-report these as gaps

Recorded so they stop being re-audited every pass:

- PLAN.md §1: hyphenation, justified text, inline/anchored blocks, block rotation at any angle,
  runtime plugins, Tagged PDF / PDF-UA, collaborative editing.
- **Rulers, draggable guides and a grid** — declined on the record at M47 §2.
- **No print dialog of our own** — M53 hands the PDF to the operating system (`PrintService`).
- **Per-page footer variation, hence "different first page"** — designed out at `ActionId.cs`.
  (M107 added the one exception anybody wanted: the cover left out.)
- **The page always prints white** (`SettingsDialog`).
- **Contour text wrap** — `Blocks.cs` states rectangle wrap *is* the whole feature.
- **The footer's wording is fixed in code** — `PageFooterRenderer` records the reasoning: what it
  says is not a preference, it is the three facts a reader needs to know which page of which issue
  they are holding.
- **Tab stops** — a typed Tab becomes a space and the Tab key leaves the frame; a tab character
  cannot be stored.
- **Paste as plain text** is not a gap: paste here is always plain.
- **The canvas CAN zoom below 100%** — `MainWindow.ZoomSteps` starts at 0.5. `ZoomLadder.cs` is the
  photo-positioning ladder (M22) and starts at 100% for its own reasons.

## 5. Suggested order for the next session

1. **Strikethrough** — proven path, seam already extracted, half a day.
2. **Table styling** (`TableStyleRef`) — closes a fourth "built and unreachable" instance.
3. **Full-screen view** — small, and §6 material like M108.
4. **Document the Alt-to-ignore-snapping escape** — a help topic, not code.
5. **Hyperlink authoring**, if the committee actually emails the PDF more than it prints it. Ask
   first; on a printed newsletter this is worth nothing.
6. **Text-frame border and fill colour** — the last cheap-looking item that is not cheap. Decide
   about a frame-style grammar before starting.

## The two rules this list exists to serve

**Ask three questions per capability, not one.** Does the model express it? Does the engine honour
it? Does a command reach it? Each layer reports itself done, which is why per-layer inspection cannot
see this category.

**Failing-first, then mutation.** Four tests in two days once passed against unfixed code. Every
milestone from M101 on has had the bug put back in to check the test noticed; do the same.
