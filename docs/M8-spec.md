# M8 — Pagination & export polish

Status: derived from the LOCKED `PLAN.md` (§3, §4, §8, §11-M8) and the M1–M7 specs. M8 is an M
milestone, so this is a working contract rather than a full design pass: it records the decisions
that are not obvious from the plan and the numbers the acceptance depends on.

Acceptance (PLAN §11-M8): *a recreated July-issue fixture exports to a PDF a reviewer judges
structurally equivalent to `Examples/July 2026.pdf`; export ≤ 2.5 MB.*

**Privacy (PLAN §0, HARD).** `Examples/July 2026.pdf` contains real members' names and phone
numbers. The committed fixture recreates its **structure** — page count, section mix, frame
geometry, where text flows — with fictional content throughout. The comparison against the real
issue is a local, human judgement; nothing derived from it enters the repo.

Repo facts this builds on (verified in-tree):

- `DocumentLayoutAdapter.BuildPlan` already walks a `linkNext` chain **across pages**: it re-resolves
  the owning page for each frame and builds that page's exclusions. Multi-page story flow is
  therefore already a property of the layout engine, not something M8 invents; M8's job is the
  editing commands that build such chains and the tests that pin the behaviour.
- `AddPageCommand` / `RemovePageCommand` exist (M2). There is no page reorder command.
- `DocumentPdfExporter` exports every page through the same `DocumentRenderSource` the screen uses,
  and already toggles `ShowEmptyPrompts` off around the export (M7 §8.4) — the precedent for an
  export-only render setting.
- `DocumentPdfParityTests` rasterises the exported PDF with `pdftoppm` and compares it to the screen
  render, downsampled 4× with a 64-per-channel threshold and a 0.2 % block budget. CI installs
  `poppler-utils` on the Linux leg only.
- `FrameEditorController` owns selection, linking (`BeginLink`/`CompleteLink`/`Unlink`) and the
  one-head-per-story invariant from M5 §8.

---

## 1. Story flow across pages

Already works; M8 pins it and adds the editing path.

1. A story's frame chain may span any number of pages in any order. `BuildPlan` follows `linkNext`
   wherever it leads and uses **each frame's own page** for exclusions — a frame on page 4 is not
   affected by a photo on page 3.
2. The chain is the ordering authority, not page order. A chain that runs page 2 → page 4 → page 3
   is legal and lays out in chain order. The UI never creates one, but a hand-edited file may.
3. `OversetInfo` continues to report the LAST frame in the chain, so the overset badge hangs where
   the text actually ran out (M5 §8.4).

## 2. Auto-flow

**`AutoFlowController.FlowSelected(blockId)`** — "Make the rest of this fit".

- Precondition: the selected block is a text frame whose story is overset.
- Each step appends ONE frame: if the chain's last frame is not on the last page, the new frame goes
  on the next existing page; otherwise a new page is added first. The new frame takes the **body
  area of the master** (the page rect inset by its margins), which is the shape a continuation frame
  wants and needs no guesswork.
- It repeats until the story is no longer overset or until **`MaxAutoFlowPages` (8)** frames have
  been added. The cap exists because a story can be unflowable — a frame narrower than one word, an
  exclusion covering the body area — and an uncapped loop would add pages until the machine died.
  Hitting the cap is reported in plain language, not silently.
- **Normative: the whole run is ONE `CompositeCommand`.** "Make the rest fit" is one user action and
  must be one Ctrl+Z, however many pages it added.
- New frames are created with `WrapMode.None`: a continuation of a story must not push its own text
  aside.
- Ids stay deterministic (`page-N`, `frame-N`, no clock, no Guid) — the M5/M6/M7 rule.

## 3. Page operations

- **`MovePageCommand(pageId, newIndex)`** is added. `Apply` removes and reinserts; `Revert` restores
  the original index. `Scope` is `PageStructure`.
- Reordering and removal **do not touch `linkNext`**. A chain is a list of block ids; moving the page
  a frame sits on changes where it prints, not what continues into what. Removing a page removes its
  blocks, which can leave a dangling `linkNext` — see §3.1.
- Page operations invalidate everything (`ChangeKind.PageStructure` already sets `_allDirty`).

### 3.1 Dangling links

`RemovePageCommand` can orphan a chain: frame A on page 1 links to frame B on page 2, page 2 goes
away. `DocumentLayoutAdapter.BuildPlan` currently throws `InvalidOperationException` from
`FindBlock` in that case — which would take down the paint pass for the whole document.

**Normative: a `linkNext` pointing at a block that no longer exists terminates the chain.** The
story becomes overset at that frame, the badge appears, and the user is told in plain language.
Losing the *continuation* is recoverable; losing the *document* is not. `RemovePageCommand` is left
alone rather than made to rewrite links, because rewriting them is not reversible in a way `Revert`
can restore faithfully.

## 4. Orphan control (and why widow control is not here)

PLAN §11-M8 lists widow/orphan as "if time". Half of it is in, and the half that is missing is
missing for a reason worth recording.

- `LayoutOptions` gains `MinLinesAtBreak` (default **2**). When a frame fills part-way through a
  paragraph and leaves fewer than that many lines behind, those lines are removed from the frame and
  the story rewinds so they travel with the rest of the paragraph.
- **Never on the last frame in a chain**: there is nowhere to push to, so pushing would not move the
  line down a page, it would push it off the end of the story into overset. A stranded line is worse
  than ugly there — it is invisible.
- **Never when it would empty the frame**: that does not fix the break, it just moves it one frame
  along and repeats.
- `MinLinesAtBreak = 0` (or 1) restores the pre-M8 behaviour exactly, which is how the M1 golden
  tests keep testing what they were written to test.

**Widow control — fewer than N lines carried FORWARD — is deferred.** The engine lays frames out in
a single forward pass, and at the moment a break is chosen the number of lines the paragraph will
occupy in the *next* frame is not yet known: it depends on that frame's own width and exclusions.
Enforcing it needs either a speculative layout of the remainder against the next frame's geometry or
a second pass that revises breaks already emitted. Both are real changes to the engine's shape, and
neither is worth destabilising the M1 line-breaking gates for during an M milestone. Recorded as a
deferral rather than half-implemented.

## 5. PDF output

### 5.1 Metadata

Taken from `DocumentMetadata`, not invented at the call site: title, lodge name as author, and a
subject naming the issue. Already wired; M8 pins it with a `pdftotext`/`pdfinfo` assertion.

### 5.2 Images — a lever that turned out not to exist

The first version of this spec claimed a 4× saving from handing Skia **encoded** images instead of
rasters, on the strength of this measurement:

| Path | PDF size |
|---|---|
| Processed pixels handed to Skia as a raster `SKImage` | 671 KB |
| Original encoded bytes handed to Skia | 169 KB |

**That comparison was invalid.** The 671 KB was a whole two-page document — five embedded font
subsets and all its text — while the 169 KB was a bare PDF containing nothing but one image. It was
measuring fonts, not photos.

Measured properly, against the same document with and without its photo: at the pixel budget the
M6 pipeline already applies (frame-derived, long edge ≤ 2048), Skia's own compression of the
downsampled raster is as good as anything re-encoding would buy, and passing an untouched original
through is usually *worse* — a 1600px photo embedded whole to fill a 250pt frame is larger than the
downsampled copy the page actually needs.

**So M8 adds no image-encoding mechanism.** The implementation existed briefly, measured nothing,
and was removed rather than kept because a spec table said it should help. The cost of a photo in
the exported issue is asserted as a test so that if this ever inverts, it fails loudly.

The real reason the export stays small is M6's pixel budget, which was already doing the work.

### 5.3 Budget

`≤ 2.5 MB` for the issue fixture, asserted in the test. The examples run 1.28–1.80 MB, so the
fixture is expected to land well under.

## 6. The issue fixture

`SampleIssue` — a **fictional** five-page trestle board in `TrestleBoard.Core.Samples`, built from
the structure of the real July issue (see the local wiki's `trestle-board-issue-structure`):

| Page | Content |
|---|---|
| 1 | CoverBanner, an opening essay flowing into page 2, a photo |
| 2 | Essay continuation, OfficersTable with text wrapping beside it |
| 3 | BirthdayList (narrow column, text flowing beside), CommitteeList |
| 4 | DistrictCalendar in Both mode, an EventCard |
| 5 | A closing piece and a submissions box |

It exercises every M7 widget, a cross-page story chain, a wrapped photo and a wrapped narrow column
— i.e. everything M8 has to prove at once. All names fictional, all phone numbers `555-01xx`.

## 7. Testing

| Project | Gates |
|---|---|
| `Core.Tests` | `MovePageCommand` in the Apply/Revert identity sweep; page-order round trip through the container. |
| `Layout.Tests` | Cross-page chains use per-frame exclusions; orphan control moves the break, spares the last frame, never empties a frame, and `MinLinesAtBreak = 0` reproduces M1 exactly; a dangling `linkNext` terminates rather than throwing. |
| `Editing.Tests` | Auto-flow clears overset, adds the fewest pages that works, is one undo step, respects the cap, and reports it. |
| `Rendering.SnapshotTests` | The issue fixture's five pages as baselines; `pdftoppm` parity on every page; the ≤2.5 MB budget; what the photo costs; metadata present. |
| `App.HeadlessTests` | Page add/remove/reorder and auto-flow reachable from the keyboard. |

## 8. Deferrals

| Item | Why |
|---|---|
| Re-encoding images on export | §5.2 — measured, made no difference, removed. |
| Automatic reflow of the whole issue on every edit | Auto-flow is a user action. Silent repagination while typing is how a layout program loses someone's trust. |
| Balancing columns across a spread | Not in PLAN §11-M8; multi-column frames are a v1 non-goal anyway. |
| Hyphenation to improve breaks | M1 deferral, unchanged. |
| Widow control (lines carried forward) | §4 — needs a speculative or second layout pass; not worth destabilising the M1 breaking gates for. |
| Rewriting `linkNext` when a page is removed | §3.1 — it cannot be reverted faithfully. |
| PDF/A, tagged PDF, colour profiles | Not in the plan; the requirement is a readable, emailable newsletter. |
| Per-image quality settings in the UI | q85 is not a decision this committee should have to make. |
