# M18 — Pictures that can be filled, swapped and captioned

**Delivered 2026-07-28.** Implements PLAN.md §11 M18. This file records what was built, where it
differs from the plan, and what is still open.

M17 was a milestone about *telling*: nothing it touched changed what the app could do. M18 is the
opposite, and the plan says so — "this is the genuine-gap milestone, where the others are mostly
affordance work". Four things were missing outright:

- **`SixPagePhotoTemplate` shipped three unfillable frames.** Since M9 the photo template has put
  `ImageFrame`s on pages 4 and 5 pointing at `photo-placeholder-1.jpg` and `photo-placeholder-2.jpg`,
  assets no package ever held. There was no replace command anywhere in the application, so
  `DocumentRenderSource.RenderImage` fell through to `RenderPlaceholder` and drew a grey rectangle
  for the life of the document. A user who chose the photo template could not put a photograph in it.
- **`ImageFrame.Caption` was collected and never drawn.** `PhotoInsertDialog` has asked for a caption
  since M6, `PhotoController.InsertPhoto` has stored it, the container has round-tripped it, and no
  code path has ever painted it.
- **`PhotoController.SetAltText` bypassed `DocumentSession`.** It assigned `frame.AltText` directly,
  which made it the one photo edit in the app that could not be undone.
- **`SetCrop` and `SetAutoLevels` had no callers at all** — a zero-caller surface, which the plan
  said would not survive the milestone.

---

## 1. Replace: one command, two names

`ReplaceImageCommand` (Core) swaps four things on an existing frame in one step: `AssetRef`,
`Recipe`, `AltText` and `Caption`. `PhotoController.ReplacePhoto` registers the new bytes with the
asset store *before* executing it, exactly as the insert path does, so the first paint after the
command can decode them.

**Geometry is deliberately untouched.** The frame a template's designer drew is the frame they
wanted; a swap that resized it to the new photograph's aspect would undo their layout every time
somebody changed their mind about a picture. **The recipe IS reset**, because a crop chosen to suit
one photograph means nothing on the next one.

**The old asset stays in the container.** That is what makes the undo lossless: `Ctrl+Z` puts the
previous `AssetRef` back and the bytes are still there, byte for byte, along with whatever crop and
auto-levels had been applied to it. `SwappingAPictureIsOneUndoStepBackToTheOldBytes` is the test.

**A replace never re-encodes.** The bytes handed in are the bytes stored, so §12 item 7's "re-crop
months later with no loss" guarantee applies to a swapped-in photograph exactly as to an inserted
one. There is exactly one encode in the application and it is on the paste path (§4).

The command is named for what is in the frame. `ActionCatalog.TitleFor(actionId, context)` is new,
and it is the first context-dependent title in the app:

| Frame | Title |
| --- | --- |
| empty | Put a picture here… |
| holds a photograph | Swap this picture… |

`EditorAction.Title` keeps the empty-frame wording, so a surface that has not been taught about
`TitleFor` — a menu header written in XAML — reads correctly rather than wrongly. `MainWindow`
rewrites the two affected menu headers in `RefreshActions`, taking the words from the catalog rather
than inventing a third spelling.

`picture.caption` gets the same treatment: "Write a caption…" becomes "Change the caption…" once
there is one.

## 2. Reachability: four ways in

1. **Format ▸ Picture ▸ Put a picture here…** — the menu home, which the M17 index tests require.
2. **Ctrl+Shift+O** — "open a picture into this frame", beside Ctrl+O for opening a newsletter.
3. **Double-click the frame.** M17 deliberately left a photo's double-click alone ("there is no one
   obvious thing it should do, and guessing wrong is worse than doing nothing"). M18 is where there
   is one. `PageCanvasControl.TryActivatePictureAt` mirrors `TryActivateWidgetAt` exactly, and the
   shell answers it by dispatching `picture.replace` **through the action runner** — so the
   availability rules apply to the gesture exactly as they do to the menu item.
4. **The "what's next" card, with nothing selected at all.** This is M13's birthday-sync idiom: the
   card is where the user is standing when they are told the photo pages are still empty, so the
   command is available there and the shell finds the frame — `PhotoController.FirstPlaceholder`
   returns the block and its page, and `MainWindow.PictureTarget` turns to that page and selects it
   before opening the file picker. It is available **only** when nothing else is selected; with a
   text frame chosen it would fill in a frame nobody is looking at.

The other three new commands are menu-only. Three more unmemorable chords would buy nothing: a
caption and a description are typed once and then left alone.

**The empty frame says all this on the page too.** `GetPlaceholderPictureRects` is a query, not a
drawing method, and `PageCanvasControl.DrawAdornments` paints "Double-click to choose a picture"
with Avalonia primitives — the M17 rule, kept for the M17 reason: there is no path from an Avalonia
`DrawingContext` to `SKDocument`, so a hint cannot print. The panel carries the same sentence
through `DescribeSelectionHint`, which until M18 answered only for widgets.

## 3. Captions, the only pixels M17–M21 may move

`CaptionLayout` (Layout) builds an ordinary one-paragraph `LayoutRequest` from the document's own
`caption` paragraph style, in a band under the frame. `DocumentRenderSource` runs it through the
same `TextLayoutEngine` every story goes through and draws it with `PageRenderer.RenderFrame`.

**No second text path.** That is the whole design: WYSIWYG is structural in this application
(PLAN.md §1) because the editor canvas and `SKDocument` replay the same draw calls. A caption drawn
by a bespoke routine would have been the first hole in that. Instead the caption is a story like any
other, and it prints because everything else does.

**How the surrounding text learns about it.** `DocumentLayoutAdapter.BuildPlans` already accepted a
rect-override map — the drag-preview mechanism from M5. Captions reuse it: `LayOutCaptions` runs
*before* the story plans are built (a caption depends only on the frame it hangs under, so there is
no cycle), and each captioned frame's entry in `_layoutRects` is its own rect grown down to the last
caption line's `BandBottom`.

`_layoutRects` is used by layout **and by nothing else**. Painting and hit-testing keep using
`_previewRects`, so:

- the picture is not stretched into its caption;
- clicking a caption is not clicking the photograph;
- and **a document with no captions produces exactly the rects it produced before M18**.

That last one is the additive guard, and it is a test rather than a hope:
`ACaptionlessDocumentLaysOutExactlyAsItDidBefore` renders page one of the sample issue with the
caption removed and with a caption of nothing but spaces and compares the PNGs byte for byte.

**Three sizing decisions, stated:**

- `GapPt = 4` between the frame and the caption band.
- `MaxLines = 3`. A caption is a line under a photograph, not an article; three is generous enough
  that nothing sensible is cut off and small enough that a paragraph pasted in by accident cannot
  silently push a column of text half a page down.
- The band offered to the engine is `lineHeight × 1.6 × MaxLines`. It is an upper bound only —
  what the text flows around comes from the line boxes the engine actually produced — so
  overshooting costs nothing and undershooting would clip a line.

`ResolveStyle` falls back `caption` → `body` → first-defined → a built-in default. A document written
by hand in a test does not carry a full stylesheet, and throwing there would turn a missing style
into a blank page.

**The scope of a caption change is `BlockGeometry`, not `BlockContent`.** A caption moves the text
around it, so editing one must relayout, not merely repaint. `SetPictureWordsCommand` carries a
geometry scope for a caption and a content scope for a description, which is spoken and never drawn.

**Baseline cost: two files, on three platforms.** `issue-page1` (the cover photograph gained "A warm
evening at the lodge." under it) and `issue-page2` (the essay it flows around starts lower). Thirteen
other baselines are untouched. Windows was baked locally; Linux and macOS were baked by the
`bake-baselines` workflow and promoted by hand, as every per-OS re-bake since M3 has been. **The same
two files moved on all three glyph rasterisers and no others** — the additive guard, restated in
bytes.

## 4. Paste and drop

**Drop lands where it was dropped.** `OnCanvasDrop` reads `e.GetPosition`, which was in the event all
along and was thrown away: every dropped photograph went to the same place in the middle of the page,
which reads as the app ignoring the gesture it just accepted. `InsertPhoto` gained an optional centre
point and clamps the frame onto the sheet — a picture dropped near the edge belongs near the edge,
and half of it hanging off the paper is not what anybody meant. `DefaultRect` remains the placement
for every keyboard path, where there is no pointer to ask.

**A drop onto a picture frame replaces what is in it**, in one undo step. It is the only thing
dropping a photograph onto a photograph can sensibly mean.

**Ctrl+V outside a piece of writing pastes a picture**, through the same ingest path a dropped or
chosen one takes — description asked for and all. A selected picture frame means "put it in here"
rather than "add another one".

A **file** on the clipboard is preferred over a **bitmap** on it: the file's own bytes land in the
package untouched. A clipboard bitmap has no file behind it and must be encoded once, here, to become
one. **That is the only encode in the application**, and it happens because there is nothing else to
store — never to "improve" a picture the user gave us.

`edit.paste` therefore stops needing a caret and starts needing a newsletter. **What is actually on
the clipboard stays the shell's business**: asking in the catalog would mean reading the clipboard on
every refresh, and the clipboard API is asynchronous while `Evaluate` is not. An empty clipboard is
answered with a sentence in the status line, which is a polite live region, rather than with nothing
happening.

## 5. The two zero-caller methods, wired

`PhotoAdjustWindow` gains "Trim the edges" — four named sliders, one per edge, capped at 0.45 of the
picture each — which is what now drives `SetCrop`. **Freeform drag-crop stays rejected**: dragging a
corner handle over a picture is exactly the fine-motor task PLAN.md §6 exists to avoid, and every one
of these sliders is reachable with the arrow keys.

`SetCrop` now executes the bare `SetImageRecipeCommand` rather than the labelled composite, so a trim
drag coalesces into one undo step the way the brightness sliders always have. An undo stack with a
step per pixel of drag is an undo stack nobody can use.

`SetAutoLevels` gets the checkbox beside them: "Brighten and balance it automatically".

"Trim the edges…" and "Adjust the picture…" open the *same* window; the difference is where the
keyboard lands, so the command the user chose is the one they are standing in.

## 6. Two things found on the way

**Six buttons across three picture dialogs had no accessible name** — `PhotoInsertDialog`'s two since
M6. Adding the dialogs to `AccessibilityTests.EveryWindow` found them immediately. A screen reader
falls back to reading the visible content, which works right up until the content is not a plain
string, and nothing was checking.

**The panel repeated itself.** With an empty frame selected, four blocked picture commands printed the
*same* refusal sentence four times: four times the reading for one fact, on a panel built for people
who find reading it work. A run of buttons blocked for the same reason now prints that reason once.
Every button still carries it in `AutomationProperties.HelpText`, so a screen-reader user hears it on
whichever one they land on — the sentence is hidden from a reader who has just read it, and from
nobody else.

## 7. What M18 did not do

- **No inline caption editing on the canvas.** A caption is edited through its dialog. Inline editing
  of widget text stayed deferred in M17 for the same reason: the payload-versus-story split makes a
  caret in generated text a three-way merge, and this caption is drawn from a block field, not a
  story.
- **No caption styling controls.** A caption is set in the document's `caption` style; changing that
  style changes every caption, which is the M14 style-first model working as designed.
- **No multi-picture insert.** One file at a time, as before.
- **A caption that overflows three lines is not marked.** It is laid out to three and the rest is
  overset with no badge, because the caption is not a story the user can navigate into. In practice
  a caption long enough to overflow is a caption in the wrong place; if this is ever seen in real
  use, the answer is a length warning in the dialog rather than a badge on the page.

## 8. Files

| Area | What changed |
| --- | --- |
| `TrestleBoard.Core` | `ReplaceImageCommand`, `SetPictureWordsCommand` |
| `TrestleBoard.Layout` | `CaptionLayout`; `MapCharacter`/`MapAlign` widened to internal |
| `TrestleBoard.Rendering` | caption layout + drawing, `IsImageBlock`, `GetPlaceholderPictureRects`, `TryGetCaptionLayout`, `GetLayoutRect` |
| `TrestleBoard.Editing` | `ReplacePhoto`, `SetCaption`, undoable `SetAltText`, `IsPlaceholder`, `HasPlaceholder`, `FirstPlaceholder`, centred insert; four `ActionId`s, three `ActionContext` fields, `TitleFor`, the photo hint, the "what's next" row |
| `TrestleBoard.App` | `PictureWordsDialog`; trim + auto-levels in `PhotoAdjustWindow`; replace/caption/describe/trim handlers; drop position and drop-to-replace; picture paste; double-click; placeholder adornment; menu items; Ctrl+Shift+O; icon decisions; the panel's repeated-reason fix |
| `tools/TrestleBoard.Screenshots` | `photo-template-placeholders` shot; M18 milestone gate; adjust-window copy |
