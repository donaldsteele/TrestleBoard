# TrestleBoard M5 — Frames & Direct Manipulation: Implementation Spec

Status: derived from the LOCKED `PLAN.md` (§2, §4, §6, §11-M5) and `docs/M4-spec.md`. Translates
M5's deliverables into implementable contracts; re-opens no locked decision.

Acceptance (PLAN §11-M5): *drag an image frame through a text column → live reflow at 60fps on a
6-page doc; every mouse action has a keyboard path.*

Repo facts this spec builds on (verified in-tree):

- `Block` already carries `FrameRect`, `ZOrder`, `WrapMode`, `WrapMarginPt`; `TextBlock` carries
  `StoryRef`, `LinkNext`.
- `MoveBlockCommand`/`ResizeBlockCommand` (`BlockRectCommand`) already exist **and already merge**
  same-type same-block executions — an arrow-nudge burst is one undo step for free.
- `SetWrapModeCommand`, `AddBlockCommand`, `RemoveBlockCommand`, `AddStoryCommand`,
  `RemoveStoryCommand`, `CompositeCommand` all exist.
- `DocumentLayoutAdapter.BuildPlans` walks `linkNext` chains and derives exclusions from blocks
  with `WrapMode.Rectangle` **and higher `ZOrder`** than the text frame.
- `DocumentRenderSource` is invalidating and caches `LayoutResult` per story; `ChangeKind.BlockGeometry`
  currently marks *everything* dirty.
- `Core.Tests.CommandTests.EveryCommandTypeHasIdentityCoverage` reflects over every `IDocumentCommand`
  and fails the build until new commands are registered.

Coordinates are typographic points (`float`), page-absolute. "Screen-constant" means a size in
points computed as `screenPt / zoom`, so it looks the same at every zoom.

---

## 1. Two modes on one canvas

The page canvas has exactly two mutually exclusive interaction modes:

| Mode | State | Owner |
|---|---|---|
| **Text** | a caret/selection inside one story | `TextEditorController` (M4) |
| **Frame** | one block selected on the current page | `FrameEditorController` (M5) |

Entering one ends the other. Neither is "the default" — the canvas starts with both idle.
Multi-block selection is **deferred** (§11); v1 selects exactly one block.

### 1.1 Pointer press resolution (page point *p*, page *i*)

1. If a block is selected and *p* hits one of its 8 handles → **begin resize drag**.
2. Else, topmost block by z whose `FrameRect` contains *p*:
   - non-text block (image / shape / widget) → **select it and begin a move drag** on this press;
   - `TextBlock`, and *p* lies in its **edge band** (§2.3) or the block is already the frame
     selection → **select it and begin a move drag**;
   - `TextBlock` otherwise → M4 path unchanged: **place the caret and start typing**.
3. No block under *p* → clear the frame selection and end any text session.

Rule 2 keeps M4's promise for the elderly-user primary path: one click in the middle of a text
frame types. Frames are grabbed by their edge — the same place the resize handles live — which
is also where the cursor changes to the move cursor.

### 1.2 Mode entry/exit by keyboard

- `Escape` in text mode ends the session **and selects the frame that was being edited** — this is
  the discoverable keyboard route from typing to frame manipulation.
- `Enter` (or `F2`) on a selected text frame starts a text session with the caret at story start.
- `Escape` in frame mode with no drag in flight clears the selection.

---

## 2. Handles and hit geometry

`TrestleBoard.Layout.Editing.FrameGeometry` (pure, no Avalonia, no Skia).

### 2.1 Handle set

```
FrameHandle: None, Body, TopLeft, Top, TopRight, Right, BottomRight, Bottom, BottomLeft, Left
```

Eight handles: four corners plus four edge midpoints, centered on the frame's corner/edge-midpoint.

### 2.2 Sizes (PLAN §6: "oversized canvas selection handles (12pt visual / 24pt hit)")

- Visual square: `12 / zoom` points on a side, centered on the handle point.
- Hit square: `24 / zoom` points on a side, centered on the handle point.
- Corners are tested before edges, so at heavy zoom-out (where hit squares overlap) a corner grab
  always wins — the ambiguous overlap resolves the way the user expects.

### 2.3 Edge band

`FrameGeometry.EdgeBandPt = 4 / zoom`: the band straddling the frame border (±4 screen points)
that grabs a text frame for moving instead of placing a caret. Non-text blocks are grabbed
anywhere in the body, so their band is irrelevant.

### 2.4 Resize math

`FrameGeometry.Resize(rect, handle, dxPt, dyPt)` moves only the edges the handle owns and clamps
each axis to `MinFrameSizePt = 12`. Edges never cross: dragging the right edge past the left stops
at the minimum width; the rect is never flipped or negative-sized.

---

## 3. Snapping

`TrestleBoard.Layout.Editing.SnapEngine`.

### 3.1 Candidate targets (the moving block's own page only)

| Priority | Source |
|---|---|
| 0 | Page margins (from the page's `PageMaster`): left, top, right, bottom |
| 1 | Page edges and page center X / center Y |
| 2 | Every other page block: left, centerX, right / top, centerY, bottom |

Master-page blocks are decoration and contribute no targets.

### 3.2 Rules

- Threshold is screen-constant: callers pass `SnapThresholdPt = 6 / zoom`.
- X and Y snap independently. Per axis the smallest |delta| wins; ties break to the lower priority
  number (a margin beats a sibling edge).
- **Move** matches the moving rect's left / centerX / right against X targets (and top / centerY /
  bottom against Y targets), then translates the whole rect by the winning delta.
- **Resize** only matches the edges the handle actually moves.
- Snapping is suppressed while `Alt` is held (free drag) and is never applied to keyboard nudge —
  a nudge must move exactly the requested distance or keyboard users cannot predict it.
- Each applied snap emits a `SnapGuide(Axis, PositionPt, FromPt, ToPt)` for the overlay.

---

## 4. Live reflow: preview, then one command

Dragging **never mutates the document**. That is what keeps undo honest and makes a 60fps loop
possible without piling 60 commands on the stack.

### 4.1 Preview override

`DocumentRenderSource.SetGeometryPreview(string blockId, RectPt? rect)` installs (or clears) a
rect override consumed by **both** paths that read a frame rect:

- `DocumentLayoutAdapter.BuildPlans(document, rectOverrides)` — frame rects and exclusion rects;
- block rendering — the dragged block itself paints at the preview rect.

### 4.2 Page-scoped invalidation (the 60fps mechanism)

A geometry change can only affect stories with a frame on the changed block's page (exclusions are
page-local by construction — `BuildExclusions` iterates `page.Blocks`). So:

- `Invalidate(BlockGeometry)` and preview updates mark dirty **only the stories that have at least
  one frame on that page**, plus the previewed block's own chain.
- Every other story replays its cached `LayoutResult`.

On the 6-page acceptance fixture this turns a full-document relayout into a single-page one.
The test gate is both the wall-clock median (§9) and a deterministic assertion that untouched
stories are not re-laid-out.

### 4.3 Commit

`EndDrag(commit: true)` clears the preview and executes exactly one `MoveBlockCommand` or
`ResizeBlockCommand` → one undo step. `EndDrag(commit: false)` (Escape, or a right-click during
drag) clears the preview and executes nothing. Keyboard nudge skips the preview entirely and
executes the command directly; `BlockRectCommand.TryMerge` collapses the burst.

---

## 5. Z-order

Page content paints in ascending `ZOrder`; z also decides wrap (a block only pushes text aside in
frames **below** it), so z-order is a layout operation, not just a paint one — the UI says so in
plain language ("Bring forward — text will wrap around this").

`SetZOrderCommand(blockId, newZOrder)` is the primitive. The controller renumbers the page densely
(`0..n-1`, document order breaking ties) and emits one `CompositeCommand` per user action:

- **Bring forward / Send backward** — swap with the nearest sibling above / below.
- **Bring to front / Send to back** — move to the last / first slot.

Dense renumbering makes repeated operations well-defined and keeps saved documents tidy.

---

## 6. Wrap toggle

`SetWrapModeCommand(blockId, WrapMode.None ↔ Rectangle, wrapMarginPt)`. Toggling on uses the
block's existing margin when non-zero, else `DefaultWrapMarginPt = 6`.

---

## 7. Add and delete frames

**Add text frame** — one `CompositeCommand` described "Add text frame":
`AddStoryCommand(empty story, one empty paragraph in the "body" style)` + `AddBlockCommand(TextBlock)`.
Default rect is `200 × 120` pt placed at the page's top-left margin corner with a cascade offset of
18pt per existing block, clamped inside the margins; z = top of the page.

**Delete frame** — one `CompositeCommand` described "Delete frame", in this order:

1. `SetLinkNextCommand(predecessor, null)` if some frame links to the victim;
2. `SetLinkNextCommand(victim, null)` if the victim links onward (so the successor detaches
   cleanly — see §8.3);
3. `RemoveBlockCommand(page, victim)`;
4. `RemoveStoryCommand(story)` when no remaining block references that story.

---

## 8. Frame linking and overset

### 8.1 Invariant

A story is displayed by exactly **one chain**: exactly one head (no inbound link) and at most one
inbound link per frame. `DocumentLayoutAdapter` assumes it (it keys plans by story id, and
`DocumentRenderSource` caches layouts by story id) — two heads on one story would silently collide.
Every link/unlink operation preserves this invariant.

### 8.2 Link

`BeginLink()` on a selected text frame arms link mode; the next click on another text frame — or
`Tab` onto it and `Enter` — completes it. Keyboard targeting uses a separate link cursor so the
source frame keeps the selection (§9). Rejected (with a plain-language message, no mutation) when
the target is not a text block, is the source, already has an inbound link, or would close a cycle.

Completing a link is one `CompositeCommand` "Link frames":
`SetLinkNextCommand(source, target)` + `SetStoryRefCommand(target, sourceChainStoryId)` +
`RemoveStoryCommand(target's old story)` when that story is now unreferenced **and empty**. A
non-empty orphan story is kept (never silently destroy text); the UI refuses the link instead and
says why.

### 8.3 Unlink

`Unlink()` on a frame with `LinkNext` is one `CompositeCommand` "Unlink frames":
`SetLinkNextCommand(source, null)` + `AddStoryCommand(new empty story)` +
`SetStoryRefCommand(detached frame, new story)`. The new story is what keeps §8.1 true — without it
the detached frame becomes a second head on the same story.

### 8.4 Overset

`LayoutResult.IsOverset` already reports "text ran out of frame". M5 surfaces it:

- a red badge with a `+` glyph at the outside bottom-right corner of the **last frame of the
  overset chain** (InDesign convention), and
- a plain-language status line: *"This text does not fit. Add another frame and link it, or make
  this frame bigger."*

Colour is never the only signal (PLAN §6): badge glyph + status text carry it too.

---

## 9. Keyboard parity (acceptance: *every* mouse action has a keyboard path)

| Mouse action | Keyboard path |
|---|---|
| Click a block to select | `Tab` / `Shift+Tab` cycle blocks on the page in z order |
| Click into text | `Enter` or `F2` on a selected text frame |
| Click out of text | `Escape` (selects the frame just edited) |
| Drag to move | Arrows = 1pt; `Shift`+arrows = 10pt |
| Drag a handle to resize | `Ctrl`+arrows = 1pt; `Ctrl+Shift`+arrows = 10pt (moves right/bottom edge) |
| Cancel a drag | `Escape` |
| Delete a frame | `Delete` or `Backspace` in frame mode |
| Add a text frame | `Ctrl+Shift+T` (Object ▸ Add text frame) |
| Toggle wrap | `Ctrl+Shift+W` |
| Bring forward / send backward | `Ctrl+]` / `Ctrl+[` |
| Bring to front / send to back | `Ctrl+Shift+]` / `Ctrl+Shift+[` |
| Link frames | `Ctrl+Shift+L`, then `Tab` to the target, `Enter` to confirm, `Escape` to cancel — while link mode is armed Tab moves a **link cursor** (`LinkTargetBlockId`) over the valid candidates and leaves the selection on the source frame; moving the selection would disarm link mode |
| Unlink frames | `Ctrl+Shift+K` |

Every row is also an Object-menu item with the same shortcut, so nothing is gesture-only.

---

## 10. Overlay rendering

`TrestleBoard.Rendering.FrameOverlayRenderer.Draw(canvas, FrameOverlay overlay, float overlayScale)`
where `overlayScale = 1 / zoom`. Like `TextOverlayRenderer` it is **never** reachable from the
export path — `DocumentPdfExporter` calls `RenderPage` only.

- Selection outline: 1 screen-pt `#2A6FCF`.
- Handles: white fill, `#2A6FCF` 1 screen-pt stroke, 12 screen-pt squares.
- Snap guides: 1 screen-pt `#E5308C` lines spanning the page on the guide's axis.
- Overset badge: 12 screen-pt `#C62828` square with a white `+`, hung outside the frame's
  bottom-right corner.
- Link badge: small `#2A6FCF` arrow at the bottom-right of a frame that has a `linkNext`.

---

## 11. Deferrals (explicitly out of M5)

Multi-block selection, group/ungroup, rotation, non-rectangular wrap, cross-page drag, user-created
ruler guides, align/distribute commands, baseline-grid snapping, and drag-and-drop of external
files (M6 owns image insertion). Overflow auto-flow — "create the next page and link it" — is M8.
