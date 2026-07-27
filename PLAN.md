# TrestleBoard — Masonic Trestle Board Desktop Editor: Master Plan

## Context

Indian Land Masonic Lodge 414 publishes a monthly 4–6 page "trestle board" newsletter (see `Examples/*.pdf`), currently produced in a legacy DTP tool and distributed as PDF by email. This project builds a purpose-built, cross-platform desktop editor so the lodge's trestle board committee — primarily elderly users — can produce each issue easily: edit text, tables, and images; use form-driven wizards for recurring data (officers, birthdays, committees, district calendar); and export a distribution-ready PDF. The app is greenfield, .NET 10, and must run on Windows, Linux, and macOS.

**User decisions locked in:**
- Editor style: **free-form DTP canvas** (Publisher-like: drag blocks anywhere, text wraps around widgets/images) — matches the existing trestle boards.
- Distribution: **GitHub Releases + Velopack auto-update**; CI on GitHub Actions.
- macOS signing: **skipped** (no Apple Developer budget); ship plain-language Gatekeeper workaround instructions.
- The **graphify** and **llm-wiki** plugins MUST be used throughout the build (integration plan in §10).
- **Privacy: real lodge data never enters git history** (see §0 — this is a hard rule enforced from M0).

## 0. Data privacy rules (hard requirements, enforced from M0)

The example PDFs contain real people's names, phone numbers, and emails. These rules apply to every milestone and every agent:

1. **`Examples/` never reaches GitHub.** `.gitignore` includes `Examples/` from the very first commit (M0 acceptance criterion: `git check-ignore Examples/` passes before the remote is added). The folder stays local-only for reference.
2. **No real personal data in wizards, templates, fixtures, or tests.** Shipped templates and all test fixtures use obviously fictional placeholders (e.g., "A. Placeholder, Worshipful Master, 555-0100"; birthdays like "Sample Brother 1/1"). Snapshot baselines, golden tests, and headless tests must be generated from fictional data only — they are committed to the repo.
3. **Knowledge-tool outputs stay local.** llm-wiki ingests the example PDFs (so agents can query domain structure), and graphify graphs the corpus — both therefore absorb real data. `.gitignore` includes `wiki/`, `raw/`, and `graphify-out/`. They are development aids, not repo artifacts.
4. **Real data lives only in the user's own `.tboard` files**, which are personal documents outside the repo.
5. **The roster file is real personal data (from M12).** `%AppData%/TrestleBoard/roster.json` and its
   `roster-backups/` ring hold real member names, birthdays, phone numbers and emails — the first such
   file the app itself creates. Rules:
   - `.gitignore` gains `roster*.json`, `roster*.xlsx`, `*.roster.bak.json`. AppData cannot be committed,
     but a user's "Save as a spreadsheet…" can land in any folder they browse to, including this one.
   - **Roster fixtures exist only in `tests/Roster.Tests`, and are fictional.** Never in `assets-src/`,
     never in templates, never in `docs/`. graphify does not read AppData, so the live roster is safe —
     a stray fixture in a scanned path is the actual exposure.
   - Roster export writes only to a user-chosen path via the save dialog. No default location beside the
     repo or the `.tboard`.
   - Never paste roster contents into a commit, test, fixture, issue, or a graphify/llm-wiki run.
   - §12's privacy gate re-runs after every roster stage.

## What the examples tell us (domain analysis)

From text extraction of the 5 example PDFs (4–6 pages each, US Letter):
- **Cover page:** lodge name, "STATED COMMUNICATION", meeting date, dinner/work times, masonic imagery.
- **Recurring structured data:** officers list (position / name / phone), birthday list (name / date, often a narrow left column), committees (name / members), 22nd District meeting-day table + dated event announcements.
- **Free content:** officer messages, essays ("The Common Gavel"), memorials, Eastern Star news, photos with captions, special announcements.
- **Layout style:** text boxes and sidebars; body text flows beside/around tables and photos. Recreating this look requires true text-wrap-around-blocks.
- Target PDF size ≈ 1.3–1.8 MB (matches examples; keep exports in this range).

---

## 1. Architecture — core decisions

### Guiding principle
**The app owns its own layout + rendering pipeline built on SkiaSharp, used identically for the on-screen editor and PDF export.** Avalonia hosts the chrome (toolbars, wizards, dialogs); the page canvas is drawn by our engine onto a Skia surface. This is the only way to get (a) text wrap around floated frames, (b) guaranteed WYSIWYG screen/PDF parity, and (c) deterministic cross-platform snapshot tests.

### Technology choices (with rationale)

| Concern | Choice | Why / Rejected alternatives |
|---|---|---|
| UI framework | **Avalonia UI 11.3+** (MIT) | Only mature .NET cross-platform *desktop* framework with Linux support; native Skia renderer with `ICustomDrawOperation` + `ISkiaSharpApiLeaseFeature` for direct SKCanvas access; headless test support. MAUI: no Linux — disqualified. Uno: WinUI-first, weaker desktop track record. |
| PDF export | **SkiaSharp `SKDocument.CreatePdf`** | Same SKCanvas draw calls as the screen renderer → parity by construction; real embedded/subset fonts, selectable text, vector output; BSD/MIT. QuestPDF rejected (imposes its own layout model — solves a problem we've already solved). iText rejected (AGPL). PDFsharp: keep in mind only for optional post-processing. |
| Image processing | **SkiaSharp only** | Already in stack; MIT/BSD; pixel-span access sufficient for auto-crop/auto-levels. Avoids extra native deps (Magick.NET ~30 MB/RID) and license tracking (ImageSharp split license). |
| Text shaping | **HarfBuzzSharp** (ships alongside SkiaSharp) | Proper glyph shaping; deterministic with bundled fonts. |
| MVVM | ~~**CommunityToolkit.Mvvm 8.4+**~~ — **not used; reference removed at M11** | Intended for source-generated observables/commands. In practice the shell was built as code-behind plus hand-rolled controllers (`TextEditorController`, `FrameEditorController`, `WidgetController`, …) and the package went 100% unreferenced through M0–M10. M11 replaces the ad-hoc enable/disable sweep with an explicit action catalog rather than a ViewModel layer (see M11's "explicit non-goal"), so the package is dropped instead of left as a dead supply-chain surface. |
| Serialization | **System.Text.Json** (source-generated) | camelCase, UTF-8, unknown-property preservation for forward compatibility. |
| Spreadsheet interchange (M12+) | **ClosedXML** (MIT) | Owner's call, 2026-07-26: take the dependency rather than hand-roll OOXML, to reduce complexity. Pure managed .NET with no native assets, so it behaves identically on win/linux/osx and does not complicate the four-RID Velopack packaging. Pulls DocumentFormat.OpenXml + SixLabors.Fonts transitively — knowingly accepted, and **the one place this table's dependency-minimalism is deliberately relaxed** (contrast Magick.NET and ImageSharp, both rejected above on exactly those grounds). Confined to `TrestleBoard.Roster`; re-measure against §9's ~90–130 MB self-contained budget × 4 RIDs at M12 start. |
| Installer/updates | **Velopack** (MIT) | One tool: Windows Setup + delta auto-update, macOS .app, Linux AppImage; feeds from GitHub Releases. |
| Fonts | **Bundled OFL fonts only** (e.g., EB Garamond/Source Serif 4 body, Cinzel display, Source Sans 3 tables) | Never use system fonts for document content → identical layout on all OSes and CI. |
| Tests | xUnit v3, Avalonia.Headless, snapshot PNG comparison | See §8. |

Pin exact versions at M0 (`global.json` for .NET 10 SDK; central package management). Verify SkiaSharp 3.x + HarfBuzzSharp packaging and PDF text embedding in the M1 spike — that's why M1 comes first.

### Non-goals for v1 (explicitly deferred)
Hyphenation; justified text; inline/paragraph-anchored blocks (all blocks are page-absolute); block rotation; runtime plugin loading; Tagged PDF/PDF-UA output (Skia can't; document it); collaborative editing.

---

## 2. Document model & file format

### Container: `.tboard` = ZIP archive (docx-style)
```
manifest.json     format name, formatVersion (semver), generator version, min-reader version, isTemplate
document.json     document tree
styles.json       named styles + theme tokens (separate so templates can share)
assets/           img-<ulid>.jpg|png — ORIGINAL untouched image bytes
thumbnails/       page-N.png 256px thumbnails (regenerable; used by recovery UI/file pickers)
```
Unknown JSON properties are preserved on round-trip (overflow bag). `formatVersion` gates migrations (`Core/Migrations/`).

### Object model
```
Document
 ├─ Metadata        { lodgeName, issueMonth, issueYear, title, meetingRule e.g. "1st Tuesday" }
 ├─ Theme           { colorTokens, fontTokens, spacingScale }
 ├─ StyleSheet      { paragraphStyles[], characterStyles[], frameStyles[], tableStyles[] }
 ├─ PageMaster[]    { size 612×792pt, margins, background/decoration blocks }
 ├─ Page[]          { masterRef, blocks: Block[] }
 └─ Story[]         rich-text streams referenced by TextBlocks

Block (abstract)    { id, frameRect(pt), zOrder, wrapMode: None|Rectangle, wrapMargin }
 ├─ TextBlock       { storyRef, columnCount=1, verticalAlign, linkNext: blockId? }
 ├─ ImageFrame      { assetRef, recipe {crop, rotationSteps, brightness, contrast, saturation, autoLevels}, fit, caption?, altText }
 ├─ WidgetBlock     { widgetType, dataVersion, data (widget-specific JSON), styleOverrides }
 └─ ShapeBlock      { kind: rule|box|decoration, stroke/fill refs }

Story               { id, paragraphs: [{ styleRef, runs: [{ text, charStyleRef?, overrides? }] }] }
```
Key semantics:
- **Stories vs frames (InDesign/Publisher model):** rich text lives in a Story; linked TextBlocks display it; overflow flows frame → `linkNext` → next page. Overflow with no next frame shows a red "overset" indicator.
- **`wrapMode=Rectangle`** is the entire float-with-wrap feature at the model level: text frames below in z wrap around the block's rect inflated by `wrapMargin`.
- **Images are non-destructive and originals are kept forever:** the full-resolution original bytes stay untouched in `assets/`; all edits (crop, scale, rotation, color) are a recipe applied at render time (cached by recipe hash); undo = revert recipe. Because originals persist in the container, and "start from last month" copies assets forward, any image can be **re-cropped, re-scaled, or re-corrected months later in a future trestle board** with zero quality loss.
- Templates are ordinary `.tboard` files with `isTemplate: true` and placeholder prompts. Shipped templates contain **only fictional placeholder data** (§0).

---

## 3. Layout engine (THE hard problem — retire first)

Custom line-layout engine, framework-independent, measuring and drawing exclusively through SkiaSharp/HarfBuzzSharp. All coordinates in typographic points (1/72"); editor applies a single zoom transform.

**Pipeline:** Story + styles + frame + exclusions → itemize runs → shape (HarfBuzz) → greedy line breaking against per-line segments → stack lines into frame, spill to linked frame → `LayoutResult` (positioned LineBoxes). `PageRenderer` walks LayoutResult and draws SKTextBlobs — identical code path for editor canvas and `SKDocument` PDF pages.

**Exclusion → segment algorithm:** for each candidate line's y-range, start with `[frame.left, frame.right]`, subtract x-intervals of every overlapping exclusion rect (blocks with wrapMode=Rectangle, higher z, inflated by wrapMargin); discard segments narrower than ~4 average char widths (prevents one-word slivers beside photos); if no usable segment, advance y. Greedy first-fit fill of segments with shaped word clusters (UAX#14-lite breaks: spaces, hyphens, post-punctuation). O(lines × exclusions) — full-document relayout per keystroke is affordable; dirty-frame invalidation as insurance.

**Line metrics:** lineHeight = style.lineSpacing × (ascent+descent+leading) of largest run; shared baseline for mixed sizes; paragraph spacing/first-line indent; align left/center/right (justify deferred — segment model supports it later). Widow/orphan control = bounded post-pass, v1.1 nice-to-have.

**Determinism:** bundled fonts loaded via `SKTypeface.FromStream`; culture-invariant serialization; baselines computed non-accumulatively. Same font bytes + same HarfBuzz + same Skia = identical pixels on Windows/Linux/macOS/CI — this is what makes snapshot tests and screen/PDF parity real.

---

## 4. Editing architecture

- Model is plain CLR, UI-agnostic, mutated ONLY through commands:
  `IDocumentCommand { Apply(doc); Revert(doc); TryMerge(next); Description }` executed by `DocumentSession` (undo/redo stacks, change events with dirty scope → layout invalidation → repaint).
- **Full undo AND redo for every action** — text edits, block moves/resizes, image adjustments, widget data changes, page operations — exposed via Ctrl+Z / Ctrl+Y (Cmd on macOS), Edit-menu items with plain-language descriptions ("Undo Move photo"), and toolbar buttons. Executing a new command clears the redo stack (standard semantics).
- `TryMerge` coalesces keystrokes (same story, contiguous, <1s apart) so Undo undoes word-bursts, not characters. Drags commit as one command on drop (live preview via transient overlay; model untouched until commit).
- Undo depth effectively unlimited.
- **Autosave + crash recovery:** every 60s + 5s-idle trigger, atomic write (temp+rename) of full `.tboard` to `<AppData>/TrestleBoard/recovery/`; deleted on clean close; on startup, surviving file triggers a large plain-language restore dialog with page-1 thumbnail. Also rotate last 5 autosaves as `.bak` beside the user's file ("Restore earlier version" menu) — protects users from their own mistakes, not just crashes.

---

## 5. Widget system (data-input wizards)

```
IWidgetDefinition { TypeId, DisplayName, IconKey, DataType (POCO), WizardDefinition, IWidgetLayouter, StyleDefaults }
```
- **To the layout engine a widget is just an opaque rectangle with wrapMode=Rectangle** — body text wraps around it like an image. The widget lays out internally (box tree via the same shaping utilities) and reports height for a given width.
- **Wizard:** declarative `WizardDefinition` (ordered steps, one question/list per screen) rendered by one generic `WizardWindow`: 20pt+ fonts, big Back/Next, review screen, fully keyboard-navigable. Re-edit = wizard pre-filled OR a big-row grid editor ("Edit list" with Add/Remove) — both emit the same data POCO via one command.
- **v1 widgets (6):** OfficersTable (position/name/phone), BirthdayList (name/date, narrow-column style), CommitteeList, DistrictCalendar (lodge/day table + dated events), EventCard (announcement box), CoverBanner (lodge/date/times, page 1).
- Each widget carries `dataVersion` for schema evolution. Registry is in-process (`TrestleBoard.Widgets`); clean interface boundary, no runtime plugin loader in v1.
- **Carry-forward:** "New issue from last month" copies widget data forward (officers/committees/birthdays barely change) — the single biggest monthly-effort win.
- **Roster projection rule (from M13).** Roster data reaches a widget **only** through a pure projection
  function that takes the member list as an explicit parameter and is invoked by a user-confirmed action.
  Never through `CreateEmptyData`. Never through `WidgetSeed`. There is **no ambient or static roster
  accessor anywhere in `TrestleBoard.Core` or `TrestleBoard.Widgets`.** This keeps §0's privacy property
  structural rather than merely tested: `WidgetSeed` stays non-personal (lodge name, issue month/year,
  meeting rule), which is what lets `TemplateTests.AssertWidgetEmptyOfPeople` keep passing untouched.

---

## 6. Accessibility (first-class, elderly users)

- **Screen readers:** `AutomationProperties` on every control; custom `AutomationPeer`s expose the canvas block tree ("Officers table", "Photo: <altText>", text content). Tested targets: Windows Narrator/NVDA + macOS VoiceOver; Linux AT-SPI is best-effort (weakest Avalonia leg — verify empirically in M9).
- **Keyboard-only operation everywhere:** every command has a menu item + shortcut; Tab cycles blocks, arrows nudge (Shift=10pt), Enter enters text edit, Esc exits. Drag-and-drop is ALWAYS an accelerator, never the only path (e.g., "Insert photo…" dialog beside drag-drop).
- **Scale:** minimum UI font 16pt (wizards/dialogs 18–20pt); app-wide UI scale 100–200%; canvas zoom 50–400% independent, "Fit page" default.
- **Themes:** Light (default), Dark, true High Contrast (7:1+); respect OS hint.
- **Hit targets:** ≥44×44px; oversized canvas selection handles (12pt visual/24pt hit).
- **Forgiveness:** unlimited undo; plain-language confirms ("Delete this photo? You can Undo this."); no icon-only buttons (icon+text); autosave per §4.
- Wizards are the a11y centerpiece: linear, one question at a time, no dragging required for any data entry.
- **Actions belong next to the object, not only in the menu bar (from M11).** The primary surface is a
  right-docked **action panel** scoped to the current selection — select a thing, and what you can do to
  it is beside it. The menu bar remains the complete, keyboard-discoverable index of every command
  (the bullet above still holds); it is no longer the place you go to *discover* what is possible.
- **Explained refusal (from M11).** An action the user can see must never fail silently:
  - In the **action panel**, actions that do not apply to the current selection are **absent**, and
    actions that apply but are blocked are shown with a plain-language reason ("This needs a picture.
    Choose one on the page first."). Nothing in the panel is ever greyed.
  - The **menu bar keeps conventional greying** — "dimmed" is a convention screen readers announce, and
    discarding it would trade one confusion for another — but every unavailable item carries the same
    plain-language reason in `AutomationProperties.HelpText`, and that reason is written to the status
    bar (already a polite live region) when the user activates it.
  - The asymmetry is deliberate: the panel is selection-scoped and headed "A photo is selected", so
    absence reads as "not about photos". A menu is a global index, so hiding items there would break the
    "every command has a menu item" guarantee above.

---

## 7. Templates & monthly workflow

- Ship 3 templates as embedded `.tboard` resources: **"Classic 414"** (recreates the existing look: cover + officers page + birthdays sidebar), "Simple 4-page", "6-page with photos".
- Start screen (large tiles): **"Start from last month"** (primary) / "Open a newsletter" / "Start from a template".
- Start-from-last-month: copy document, bump issue month/year, recompute date-bound fields from `meetingRule` (e.g., "1st Tuesday" → April 7th 2026), carry widget data forward, reset articles to placeholder prompts ("Write the Worshipful Master's message here…").
- Masonic ornaments/clip art bundled inside template `assets/` (no special mechanism).

---

## 8. Testing strategy

- **Unit (xUnit v3):** layout golden tests (text+styles+frame+exclusions → exact LineBoxes); command property tests (`Apply;Revert` == identity) for 100% of command types; serializer round-trip incl. unknown-property preservation; auto-crop/auto-levels fixture assertions (crop rects, histogram bounds — not pixels).
- **Rendering snapshots:** fixture docs → PNG via offscreen SKSurface vs committed baselines (small tolerance + max-diff-count); run on the full 3-OS matrix to prove determinism; upload actual-vs-expected diffs as CI artifacts on failure.
- **WYSIWYG parity test (the guarantee):** export fixture to PDF, rasterize with `pdftoppm` (Linux CI job only), compare against screen-render snapshot.
- **UI smoke (Avalonia.Headless.XUnit):** boot app, open template, type into text block, run a wizard end-to-end, undo, save, reopen, assert content.
- **CI (GitHub Actions):** matrix `[windows-latest, ubuntu-latest, macos-latest]` build+test on every push; release workflow on tag (see M10).

---

## 9. Solution structure

```
TrestleBoard.sln
 src/
  TrestleBoard.Core          model, styles, stories, commands, DocumentSession, .tboard zip
                             serialization, migrations, autosave.        Deps: BCL only.
  TrestleBoard.Layout        shaping, line breaker, exclusion segments, pagination → LayoutResult.
                             Deps: SkiaSharp, HarfBuzzSharp, Core.
  TrestleBoard.Rendering     PageRenderer (→ SKCanvas), bundled font store. Deps: SkiaSharp, Layout, Core.
  TrestleBoard.Imaging       decode/EXIF, recipes, auto-crop, auto-levels, caches. Deps: SkiaSharp.
  TrestleBoard.Widgets       IWidgetDefinition, registry, 6 built-ins.     Deps: Layout, Core, Roster (M13).
  TrestleBoard.Roster        member address book, CSV/XLSX import + export, name matching,
                             backup ring.  (M12)                           Deps: ClosedXML only — a leaf.
  TrestleBoard.Export.Pdf    SKDocument export, metadata.                  Deps: Rendering.
  TrestleBoard.App           Avalonia shell, PageCanvasControl, wizard host, start screen, themes,
                             a11y peers, settings.  ONLY project referencing Avalonia.
 tests/
  Core.Tests / Layout.Tests / Imaging.Tests / Widgets.Tests / Roster.Tests (M12)
  Rendering.SnapshotTests (Baselines/ committed)
  App.HeadlessTests
 assets-src/                 OFL fonts, template sources, fixture images
 .github/workflows/          ci.yml, release.yml
```
Dependency rule: `Core` references nothing; layout/render stack runs headless (enables PDF export, snapshot CI, and a free CLI batch-export bonus).

**`TrestleBoard.Roster` placement (M12).** The roster is *app-level* state, not document state, and it
gets its own project rather than living in `Core` or `App`:
- Not `Core` — `Core` is the document model. A roster type there invites someone to hang a roster
  reference off `Document` and re-couple app state to file state.
- Not `App` — it could not then be unit-tested without an Avalonia session, and the import/matching
  engine is exactly the code that deserves fast plain tests.
- A standalone BCL+ClosedXML leaf lets a reviewer point at one project and say *"this is the only code
  that touches real people"* — worth a great deal under §0.

The one new edge is `TrestleBoard.Widgets → TrestleBoard.Roster` (M13, for the birthday projection only).
`Roster` is a leaf, so no cycle. **`TrestleBoard.Editing` must not reference `Roster`** — the projection
is invoked from `App`, preserving `Editing`'s deliberate ignorance of `TrestleBoard.Widgets`.

**Image pipeline details:** auto-crop = downscale to 256px → Sobel energy map + skin-tone bonus (lodge photos are mostly people) → slide target-aspect windows, score energy inside minus border-cut penalty → propose crop, user adjusts in preview. Auto-levels = per-channel 0.5% percentile clip + linear stretch, with luminance-only mode to avoid tint shifts. UI: one big **"Fix photo"** button (auto-crop-to-frame + auto-levels) as the primary path; "Adjust…" panel with three large sliders behind it.

**Packaging:** `dotnet publish` self-contained single-file for win-x64, linux-x64, osx-x64, osx-arm64; trimming OFF in v1 (~90–130 MB acceptable). Velopack: Windows Setup.exe + delta auto-updates, macOS .app, Linux AppImage; updates fed from GitHub Releases. Unsigned: document SmartScreen "More info → Run anyway" and macOS right-click → Open with screenshots. Associate `.tboard`.

---

## 10. Mandatory plugin integration (graphify + llm-wiki)

Both plugins are required tooling for this project and are woven into the workflow:

**Privacy note (§0 rule 3):** both tools ingest the example PDFs and therefore contain real personal data. `wiki/`, `raw/`, and `graphify-out/` are gitignored from M0 — local development aids only, never pushed.

### llm-wiki — project knowledge base (domain + decisions)
- **At M0:** run `/wiki:init` in the repo, then `/wiki:ingest` each of the 5 example PDFs (captures issue structure, recurring sections, officer rosters, style observations) and ingest this plan document.
- **Ongoing:** after each milestone, ingest the milestone's design notes/ADRs (e.g., "M1 layout engine findings: SkiaSharp 3.x shaping quirks"). Use `/wiki:query` when implementing (e.g., "what sections does a trestle board cover page contain?", "what did we decide about widow control?").
- **Hygiene:** `/wiki:lint` at the end of every even-numbered milestone; `/wiki:stats` at M5 and M9 to check scaling thresholds.

### graphify — living codebase knowledge graph
- **After M2 (first substantial code):** run `/graphify .` to build the initial graph (AST extraction is free/deterministic for C#).
- **After every subsequent milestone:** `/graphify . --update` (incremental).
- **During development:** agents answer architecture questions via `/graphify query` (e.g., "what depends on LayoutResult?", "trace the path from PageCanvasControl to LineBreaker") instead of ad-hoc greps once `graphify-out/` exists.
- **At M9:** `/graphify . --wiki` to generate the agent-crawlable architecture wiki for future maintenance sessions.
- Add `graphify-out/` refresh guidance to the project `CLAUDE.md` created in M0.

---

## 11. Milestones (risk-first, agent-executable)

Each milestone lists: goal, key deliverables, acceptance criteria, size, and **recommended sub-agents + models**. General pattern per milestone: (1) optional Plan agent refines the milestone into tasks, (2) implementation by main session or general-purpose agents, (3) `caveman:cavecrew-reviewer` reviews the diff, (4) plugin refresh (graphify `--update`, wiki ingest of decisions).

Model guidance: **Fable/Opus** for high-risk algorithmic work (layout engine, text editing, parity); **Sonnet** for well-specified implementation and reviews; **Haiku** for lookups/searches via `cavecrew-investigator`/`Explore`.

---

### M0 — Scaffold, CI, knowledge base (S)
**Goal:** real skeleton, green CI, plugins initialized, privacy rules enforced.
**Deliverables:** git init with `.gitignore` covering `Examples/`, `wiki/`, `raw/`, `graphify-out/` **in the first commit, before any remote is added**; GitHub repo; solution per §9 with empty projects + `global.json` (pin .NET 10 SDK) + central package management + analyzers/editorconfig; Avalonia window opens; GH Actions 3-OS build+test matrix; project `CLAUDE.md` (build commands, architecture summary, plugin usage rules, **§0 privacy rules restated so every future agent session sees them**); `/wiki:init` + ingest 5 example PDFs + ingest this plan.
**Acceptance:** CI green on all 3 OSes; `dotnet run` shows a window; wiki answers "what's in a trestle board cover page?"; `git check-ignore Examples/ wiki/ graphify-out/` all pass and `git log --all` contains no example PDFs or real names/phone numbers.
**Agents:** main session with **Sonnet** (scaffolding is well-specified); `claude-code-guide` (Sonnet) if GH Actions/Velopack config questions arise.

### M1 — Layout engine spike (L) ⚠ THE RISK-RETIREMENT MILESTONE
**Goal:** prove text-wrap-around-exclusions with screen/PDF parity, fully headless — before any UI investment.
**Deliverables:** `TrestleBoard.Layout` (bundled font store, HarfBuzz shaping, UAX#14-lite breaks, §3 segment algorithm, LineBox model, paragraph styles); `TrestleBoard.Rendering` (PNG via SKSurface); `TrestleBoard.Export.Pdf` (same renderer → SKDocument); golden line-box tests; snapshot tests; PDF-vs-PNG parity test.
**Acceptance:** fixture "text column with two exclusion rects" renders correctly to PNG and PDF; snapshots byte-identical across the 3-OS CI matrix; PDF text is selectable with embedded subset fonts.
**Agents:** **Plan agent (Opus)** first to spec the LineBreaker API; implement in main session with **Fable/Opus** (highest-risk algorithmic code in the project); **cavecrew-reviewer (Sonnet)** on the diff; **general-purpose (Sonnet)** for test-fixture generation.

### M2 — Document model & file format (M)
**Goal:** real model + `.tboard` round-trip + undo infrastructure.
**Deliverables:** Core model per §2; System.Text.Json source-generated serialization; zip container; migrations scaffold; `DocumentSession` + command pattern with keystroke coalescing; unit tests (round-trip, unknown-property preservation, `Apply;Revert`==identity for every command type).
**Acceptance:** fixture document built in code → save → reload → identical relayout; all command types have Revert tests.
**Post-milestone:** first `/graphify .` run; wiki-ingest format decisions.
**Agents:** implement with **Sonnet** (well-specified from §2); **cavecrew-reviewer (Sonnet)**; **cavecrew-investigator (Haiku)** for cross-referencing model usage during review.

### M3 — Read-only viewer (M)
**Goal:** see documents in the app.
**Deliverables:** `PageCanvasControl` (Skia lease via `ICustomDrawOperation`; WriteableBitmap fallback documented), zoom/fit, page navigation, File→Open; code-generated fixture approximating a real trestle-board page (photo exclusion + wrapped text + table block).
**Acceptance:** fixture renders on screen identical to its PDF export (shared snapshot); pan/zoom smooth on a 6-page doc.
**Agents:** **Opus** for the canvas control (compositor/lease subtleties); **Sonnet** for file-open plumbing; **cavecrew-reviewer (Sonnet)**.

### M4 — Text editing (L)
**Goal:** click-in, caret, type — the WYSIWYG heart.
**Deliverables:** hit-testing (point → story position via LineBoxes), caret/selection rendering, keyboard input + navigation **including across wrap segments** (the subtle part), clipboard, character/paragraph style application UI, coalescing commands, undo/redo UI.
**Acceptance:** headless test types a paragraph around an exclusion and undoes in word-chunks; caret never lands inside an exclusion; arrow-key navigation crosses segment boundaries correctly.
**Agents:** **Plan agent (Opus)** for caret/selection model spec; implement with **Fable/Opus** (segment-aware caret math is the second-riskiest code); **cavecrew-reviewer (Sonnet)**; **general-purpose (Sonnet)** for headless test authoring.

### M5 — Frames & direct manipulation (M)
**Goal:** the DTP interactions.
**Deliverables:** block selection, move/resize with oversized handles, snap to margins/guides, z-order, wrap toggle, live reflow during drag (preview overlay, single commit command), keyboard equivalents (Tab-cycle, arrow-nudge), add/delete text frames, frame linking UI + overset indicator.
**Acceptance:** drag an image frame through a text column → live reflow at 60fps on a 6-page doc; every mouse action has a keyboard path.
**Post-milestone:** `/wiki:stats` checkpoint.
**Agents:** **Opus** for drag/reflow interaction loop; **Sonnet** for handles/snapping/z-order; **cavecrew-reviewer (Sonnet)**.

### M6 — Imaging (M)
**Goal:** photos that look good with one button.
**Deliverables:** `TrestleBoard.Imaging` (EXIF-aware decode, recipes, §9 auto-crop, auto-levels, slider filters, recipe-hash cache); drag-drop insert + "Insert photo…" dialog; **"Fix photo"** one-click; crop-adjust preview UI; altText prompt on insert; all non-destructive.
**Acceptance:** drop fixture photo → auto-cropped to frame aspect; auto-levels fixtures pass histogram assertions; originals byte-identical in container; every edit undoable.
**Agents:** **Sonnet** (algorithms are classic and fully specified); **cavecrew-reviewer (Sonnet)**; **Explore (Haiku)** if SkiaSharp API lookups needed.

### M7 — Widget system & wizards (L)
**Goal:** the six widgets + the accessibility-centerpiece wizard.
**Deliverables:** `IWidgetDefinition`/registry; generic `WizardWindow` (big fonts, one question per screen, review step, full keyboard); grid re-editor; OfficersTable, BirthdayList, CommitteeList, DistrictCalendar, EventCard, CoverBanner with themed rendering; wrap-as-exclusion integration; `dataVersion` handling.
**Acceptance:** headless test drives the Officers wizard end-to-end → styled table on page with body text wrapped around it; re-edit pre-fills; widget data survives save/reload; all wizard defaults and test fixtures use fictional data only (§0 — wizards start EMPTY or with obvious placeholders, never pre-populated with real names/numbers).
**Agents:** **Plan agent (Opus)** for WizardDefinition schema; **Fable/Opus** for the generic wizard host + widget layouter integration; **general-purpose agents (Sonnet), one per widget in parallel** for the six widget implementations (they're independent once interfaces exist); **cavecrew-reviewer (Sonnet)**.

### M8 — Pagination & export polish (M)
**Goal:** whole 4–6 page issues, distribution-quality PDF.
**Deliverables:** multi-frame story flow across pages; auto-flow command (overflow → create next page + linked frame); add/remove/reorder pages; widow/orphan pass (if time); PDF metadata; JPEG passthrough verification + file-size check vs ~1.5 MB examples; `pdftoppm` parity test in CI.
**Acceptance:** a recreated July-issue fixture exports to a PDF a reviewer judges structurally equivalent to `Examples/July 2026.pdf`; export ≤ 2.5 MB.
**Agents:** **Opus** for pagination/reflow; **Sonnet** for metadata/size work; **cavecrew-reviewer (Sonnet)**.

### M9 — Templates, workflow, autosave, accessibility hardening (M/L)
**Goal:** usable by the actual committee.
**Deliverables:** 3 templates (build "Classic 414" by hand IN the app — dogfooding; placeholder data only per §0, since templates are embedded in the repo); start screen; start-from-last-month with carry-forward + meeting-rule date bumping; autosave/crash recovery/rotating .bak per §4; Light/Dark/High-Contrast themes; UI scale setting; automation-peer pass over canvas; full keyboard audit; NVDA + VoiceOver manual test script (written + executed).
**Acceptance:** kill the process mid-edit → relaunch offers recovery with thumbnail, ≤60s data loss; complete a full issue keyboard-only; NVDA reads every control on the main window.
**Post-milestone:** ~~`/graphify . --wiki` (architecture wiki for future maintenance); `/wiki:lint`~~ — **deferred to M10** (owner's call, 2026-07-26); see M10's post-milestone step.
**Agents:** **Opus** for a11y peers + recovery flow; **Sonnet** for templates/start screen; **general-purpose (Sonnet)** to author the manual a11y test script; **cavecrew-reviewer (Sonnet)**.

### M10 — Packaging & release (S/M) — **delivered 2026-07-26**
**Status:** implemented; design and open items in `docs/M10-spec.md`. Two acceptance items are
hardware-bound and stay open there (§6): the clean-machine install runs on fresh Windows/macOS
machines, and the SmartScreen/Gatekeeper screenshots for `docs/INSTALL.md`. Everything else ships —
Velopack packaging for the four RIDs, the tag-triggered release workflow, background auto-update
applied on close, and the `.tboard` association on all three platforms.
**Goal:** installable by an 80-year-old.
**Deliverables:** Velopack packaging for 4 RIDs; GH Actions release workflow on tag; auto-update wired to GitHub Releases; `.tboard` file association; plain-language install instructions with SmartScreen/Gatekeeper screenshots (no code signing — documented workarounds).
**Acceptance:** on fresh Windows and macOS machines: download → install → open template → export PDF following only the written instructions; pushing a new tag produces an update an installed copy picks up automatically.
**Post-milestone:** `/graphify . --update` and `/graphify . --wiki` (architecture wiki for future maintenance); ingest the M9 AND M10 decisions into llm-wiki; `/wiki:lint`. **Carried over from M9** — M9's post-milestone plugin pass was deliberately deferred here rather than run at the time (owner's call, 2026-07-26), so this step covers both milestones.
**Post-milestone status (2026-07-26):** done. graphify updated (3141 nodes / 6843 edges) and the
architecture wiki regenerated (201 articles) — the incremental pass was **AST-only**, so the 12
changed docs and 33 changed baseline images stay unstamped in the manifest and are re-queued for
the next semantic run. llm-wiki has `m9-decisions` and `m10-decisions`; `/wiki:lint` is clean apart
from four long-standing orphan monthly-issue source pages, left as they are rather than wired up to
satisfy the linter.
**Agents:** **Sonnet** throughout; `claude-code-guide` (Sonnet) for Velopack/Actions specifics; **cavecrew-reviewer (Sonnet)**.

---

> **M11–M13 were added 2026-07-26, after v0.1.0 shipped**, from the owner's review of the finished app.
> Two problems drove them: (1) every action lives in the menu bar and items silently grey out — 47 menu
> items across 8 menus, an "Object" menu of 14, and ~30 hand-rolled `IsEnabled =` assignments with no
> explanation attached; (2) the same person's name is typed up to three times per issue, because
> `OfficerEntry.Name`, `BirthdayEntry.Name` and the bare strings in `CommitteeEntry.Members` have no
> linkage and the app has no member store at all.
>
> **These milestones overturn `docs/M7-spec.md`'s deferral** of "Import of officers/birthdays from CSV or
> a paste — the wizard is the entry path". That deferral is void as of M12; it is marked so in the M7
> spec. Do not re-defer it.

### M11 — Contextual action panel (M)

**Goal:** you click a thing, and what you can do to it is right there beside it — not hunted for in a menu.

**Deliverables**
- **Action model in `TrestleBoard.Editing`** (new `Actions/` folder — no Avalonia there, so every reason
  string is headlessly testable): `ActionId`; `EditorAction(Id, Title, ShortDescription, Group,
  DisplayGesture, IsPrimary)`; `ActionAvailability` = `Available | NotApplicable | Blocked(reason,
  remedyId?)`; `ActionContext` (flat snapshot: selection kind, editing state, text selection, page
  position, undo/redo + descriptions, widget type, overset); `ActionCatalog.Evaluate/ForSelection`.
  **The split that makes this work:** `Editing` owns *"can I, and if not, why not, in plain English"*;
  `App` owns *"how"*, via one `Actions/ActionRunner.cs` map from `ActionId` to handler.
- **Right-docked action panel**, grouped by what is selected, its heading a polite live region ("A photo
  is selected") so a screen-reader user hears what became possible. Collapsible; state in `AppSettings`.
- **Context flyout** on right-click and Shift+F10, built from the same catalog. `PageCanvasAutomationPeer`
  stops returning `false` from `ShowContextMenuCore` — today a screen-reader user pressing the
  Applications key gets nothing at all.
- **F6 / Shift+F6** cycles focus regions (canvas ↔ panel ↔ toolbar ↔ menu), exposed as
  View → "Move to the next part of the _window" so §6's menu-item guarantee still holds.
- **Object menu → "This item"**: 14 flat items re-nested into 6 direct entries plus 3 submenus (Picture,
  Text flow, Arrange). Nothing is removed — the four z-order items simply go one level deeper.
- **Toolbar trimmed** from 18 controls to ~9 (Open, Undo, Redo, page nav, zoom, Fit page). Bold, Italic,
  paragraph style, wrap, insert and arrange all move into the panel. Note `ParagraphStyleCombo` is
  currently the **only** command with no menu path at all — add Format → "Paragraph _style ▸" to close
  that §6 hole while we are here.
- **Explained refusal** per §6, replacing `UpdateEditChrome`/`UpdateFrameChrome`'s ~30 `IsEnabled =`
  assignments with a single `RefreshActions()` that feeds panel, menu and flyout from one context.
- **Keyboard dispatch table** replacing the 126-line `case Key.X when …` switch in `OnWindowKeyDown`.
- **"What's next" card** as the panel's no-selection state: a computed checklist, one row per suggestion
  with a one-sentence why. Sources: a story still holding the carry-forward article prompt; a birthday
  list whose source month ≠ issue month (M13); overset text; an empty cover meeting date; PDF not yet
  exported this session; roster empty while a people-widget is on the page (M12). This is where the
  monthly workflow finally becomes visible outside the start dialog.

**Test amendments — one hard conflict, handle it deliberately.**
`KeyboardAuditTests`' source-scraping test regex-reads `MainWindow.axaml.cs` for `case Key.X when …` and
fails if an unshifted case precedes its shifted twin. Moving to a gesture table deletes those case sites,
so the test fails with the misleading *"only N key cases found — the handler was not read"*. Replace it
with a **behavioural** test: iterate the registered gestures, synthesise each key press through the
headless window, assert the invoked `ActionId`. That is strictly stronger — the regex catches exactly one
shadowing pattern; this catches every mis-dispatch. **Sequencing rule: land the replacement while the
switch still exists and both pass, then delete the switch and the regex test in the same commit.** Keep
`ControlShiftYFitsToContentsRatherThanRedoing` and `ControlYStillRedoes` verbatim as named regressions.

Separately — and this is the fair trade for amending that test — `AccessibilityTests`' automation-name
sweep walks `MainWindow` only, so `WizardWindow`, `WidgetGridWindow`, `PhotoAdjustWindow`, `SettingsDialog`
and `StartDialog` have **never been checked**. Parameterise it over a window list. Add a sibling test that
no two items within one menu share an access key (untested today, and likelier with three new submenus).

**Acceptance**
- Select each block kind (text, photo, each of the six widgets): every action offered in the panel is
  performable, and **no panel control is ever greyed**.
- Machine-checked version of the owner's complaint: no `Blocked` availability may carry an empty reason
  string, and after opening the sample no action-panel control has `IsEnabled == false`.
- Keyboard-only run reaching and invoking every panel action.
- Manual NVDA pass at 200% UI scale in High Contrast. **The two things to validate empirically are the
  panel-heading live region and the greyed-with-`HelpText` menu behaviour** — add both to
  `docs/accessibility-test-script.md`.

**Chrome budget — verify, do not assume.** Menu + toolbar + status bar + a ~320px panel, at 16–20pt with
44px targets, at 200% scale in the current 1100×800 default window, leaves the canvas roughly 40% of the
frame. Required mitigations: a fourth `LayoutTransformControl` (`PanelScale`) so the panel honours UI
scale (the canvas stays deliberately excluded); the panel auto-collapses to a "What can I do? ▸" button
below ~900px of available width; default window grows to 1280×860. Also note the floating on-canvas
"Edit this ▸" affordance must be **positioned** in canvas coordinates but **sized** in UI units, or it is
6px tall at 50% zoom — implement it as a sibling overlay, not something Skia draws.

**Explicit non-goal: no MVVM rewrite.** Rewriting 1290 lines of code-behind into ViewModels buys nothing
the user can see and puts the whole headless suite at risk. The action catalog is the useful half of
"commands" — declared availability and plain-language descriptions — without the binding machinery.
`CommunityToolkit.Mvvm` stays unused and its reference is removed (see §1).

**Post-milestone:** `/graphify . --update`; ingest the M11 decisions into llm-wiki.
**Agents:** **Opus** for the action model, panel and keyboard dispatch — this is the risky one, touching
every menu item, the dispatcher and two constraining tests; **Sonnet** for the menu restructure and
toolbar trim; **cavecrew-reviewer (Sonnet)**.

### M12 — Lodge address book (L)

**Goal:** import the member list once, from the spreadsheet the committee already keeps; never type a
name twice again.

**Storage.** `%AppData%/TrestleBoard/roster.json`, beside the existing `settings.json`, plus a
`roster-backups/` ring of 10. Velopack installs to `%LocalAppData%` — a different root — so **auto-update
cannot touch the roster**. This file holds data that exists nowhere else and has no cross-session command
stack, so the backup ring is its only protection: mirror §4's rotating-`.bak` philosophy, atomic
temp+rename writes, and `AppSettings.Load`'s never-throw-on-corrupt contract.

**Schema** (camelCase, source-generated `System.Text.Json`, `[JsonExtensionData]` throughout — same
conventions as the document model):

```
Member { id, displayName, sortName?, birthMonth?, birthDay?, phone?, email?,
         office?, degreeDate?, degreeKind? ("raised"|"initiated"), isActive, notes?, extraProperties }
```

`id` is stable and never reused, so it survives renames. `office` is free text, not an enum — titles
drift. One date plus a kind rather than two date fields, which keeps the add-a-person form to seven
fields. **No birth year**, matching the existing month/day-only rule for birthdays.

**Import flow** — a `RosterImportWindow` over a headless `RosterImportSession`. It borrows `WizardWindow`'s
visual language (20pt, one question per screen, big Back/Next, review) but **does not** go through
`WizardDefinition`/`WizardSession`: the mapping step is dynamic (columns discovered at runtime) and the
preview is a table, so bending the widget wizard to fit would make both worse. It *does* copy the
architectural trick — all state in the headless session, the window a mere renderer — so the whole flow is
unit-testable without Avalonia. Six screens:

1. **"Where is the list?"** — one big Browse button. *"Nothing changes until you say so at the end."*
2. **"Which sheet?"** — only for XLSX with more than one; skipped otherwise.
3. **"Which row has the column titles?"** — first 8 rows shown as a table, one big radio each, plus
   "There are no column titles". Auto-guessed and pre-selected. Almost no import UI offers this, and it
   is where confusion starts.
4. **"Match up the columns."** — inverted from the usual source→target grid: **one question per lodge
   field**, seven rows, e.g. *"Name — which column has it?"*. Each dropdown item shows column letter +
   header + the first two values, so the user **recognises data** rather than decoding a header.
   Auto-guessed by fuzzy header match (`name|member|brother`, `birthday|birth|dob|bday`,
   `phone|tel|cell|mobile`, `email`, `office|title|position`, `raised|initiated|degree`) under
   *"We guessed these. Change any that are wrong."* Only Name is required; every other field offers
   "Not in this file".
5. **"Have a look before we add these."** — the first 20 rows as they will be stored, plus plain counts:
   *"24 people are new."* / *"6 are already in your list — we'll update their details."* (with a
   "Leave them alone instead" toggle) / *"3 rows we couldn't use."* (expandable, one line each, plus
   **Save these to a file** so nothing is silently lost). Near-duplicates ask *"Are these the same
   person?"* with **[Same person] / [Different person]** — never auto-merged.
6. **"Done."** — *"Your address book now has 118 people."* plus **Undo this import**.

**Merge policy — the most important rule set in the milestone.** Match key in strict order: exact `id` →
exact normalised email → exact normalised name (trim, collapse whitespace, case-fold, strip punctuation
and Jr/Sr/II/III, normalise `Last, First` ↔ `First Last`) → otherwise new. Then:
- **Update by match; never replace the file; never delete.** Import never removes a member; deletion is a
  deliberate per-person act in the People window.
- Fields whose column was mapped overwrite. **Fields whose column was not mapped are left alone** — so
  importing a phone-only list cannot wipe everyone's birthdays.
- Auto-merge on exact match only. Everything fuzzy is a **question, never an action**: merging two
  brothers would corrupt the book and print a wrong name.

**People window.** A new top-level **`_People`** menu (People… `Ctrl+Shift+R` / Import from a file… /
Save as a spreadsheet… / Undo the last change / Restore an earlier version…) — top-level rather than
buried in File, because this becomes one of the five things the app does. A 24pt search box with live
filter and a spoken result count; list rows ≥56px showing `Name — office — birthday` so the useful facts
are visible without clicking; and a **single-page** seven-field form on the right — not a wizard, because
the user is in "look someone up and fix a phone number" mode. The same form is reused for Add a person,
so it is learned once. Delete confirms in plain language: *"Newsletters you already made will not change."*

**Roster undo — deliberately NOT `IDocumentCommand`.** Four reasons, and this is a rule, not a preference:
`IDocumentCommand.Apply` takes a `Document` and the roster is not one; `CommandTests`' coverage gate would
demand `Apply;Revert == identity` against a `Document` fixture, a type mismatch enforced by the build;
roster edits are coarse and must survive across sessions, which suits a snapshot ring better than an
in-memory stack; and — decisively — sharing `DocumentSession` would make **Ctrl+Z in the newsletter undo
an address-book edit**, precisely the class of surprise M11 exists to eliminate.

> **Ctrl+Z never crosses the roster/document boundary.**

The People window gets its own single-level "Undo the last change" plus "Restore an earlier version…",
listing the 10 backups by date and member count. Snapshot restore is the right undo idiom for this
audience anyway.

**XLSX via ClosedXML** (§1). Export `Lodge-address-book-YYYY-MM-DD.xlsx`, one sheet "People", columns:
TrestleBoard ID, Name, Birthday, Phone, Email, Office, Raised or initiated, Date, Active. **Every cell
written as text** — a real date cell forces a year and renders `7/4/1900`, and a numeric phone becomes
scientific notation. Writing the ID column makes export → edit in Excel → re-import lossless, which is
the workflow a lodge secretary will actually use.

Known import hazards, each owed a fixture:
- **Excel dates arrive as serial numbers** (`45123`, not `7/4`) — the single most predictable failure. A
  "Birthday" column will very often be a real date cell.
- `.xls` (old binary) → refuse plainly: *"This is an older kind of spreadsheet. Open it in Excel and
  choose Save As → Excel Workbook (.xlsx), then try again."* Password-protected → refuse plainly.
  `.xlsm` → accept.
- CSV: BOM, CRLF, quoted fields with embedded commas and newlines, semicolon delimiter from European
  Excel (sniff from the header row), and Excel's `="555-0100"` text idiom.

**Testing** (`tests/Roster.Tests`, new project — add to `TrestleBoard.slnx` and the CI matrix):
- **Idempotence is the key test:** import a file, import the identical file again, assert zero changes.
  That one test guards the entire merge policy.
- RFC4180 CSV fixtures covering every hazard above.
- **XLSX fixtures produced by real tools** (Excel *and* LibreOffice), fictional people only —
  round-tripping our own writer proves nothing about the reader.
- Name-matching property tests, plus a negative suite asserting two distinct fictional brothers never
  auto-merge.
- Date-serial conversion table; backup-ring rotation; corrupt-file recovery never throws.
- An assertion that **no roster file ships anywhere in the repo** (§0 rule 5).

**Acceptance:** import a fictional 100-person list as CSV and as XLSX, get identical results; re-import
changes nothing; export → edit in Excel → re-import moves only the edited fields; find a person by typing
three letters; all of it completable keyboard-only.

**Post-milestone:** `/graphify . --update`; ingest the M12 decisions into llm-wiki; `/wiki:lint`
(even-numbered milestone).
**Agents:** **Opus** for the import session and merge policy; **Sonnet** for the People window and export;
**cavecrew-reviewer (Sonnet)**.

### M13 — Roster-driven widgets (M)

**Goal:** the address book actually fills things in — birthdays generate themselves, officers and
committees stop being retyped.

**Birthdays — a materialised snapshot, not a live query.** `BirthdayListData.Entries` stays exactly what
it is today: the printed truth, stored in the document. A live query would print December's birthdays
when you reopen March's issue in December. The layouter's `(Month, Day, Name)` sort, carry-forward, PDF
export and the privacy gate are therefore all unaffected in shape.

`dataVersion` 1→2, purely additive: `source ("manual"|"roster")`, `sourceMonth`, `generatedUtc`,
`rosterFingerprint`, `removedMemberIds[]` on the data; `memberId` and `isManual` on each entry.
`removedMemberIds` earns its keep — without it, every re-sync resurrects the brother the user
deliberately deleted.

**The privacy gate is undisturbed.** `TemplateTests` asserts zero birthday entries in templates; the new
fields are additive and templates ship `source: "manual"` with zero entries, so it passes untouched. Its
`default:` branch only fires for a *new widget type*, and M13 adds none.

**Projection** lives in `TrestleBoard.Widgets/Roster/`: `BirthdayRosterProjection.Plan(current, members,
month)` — a **pure function** with the member list passed explicitly, per §5's roster projection rule.
Returns `{ Additions, Removals, KeptManual, Result, Fingerprint }`. It **commits through the existing
`WidgetController.ApplyWidgetData`** — no new `IDocumentCommand` type, deliberately, so `CommandTests`'
coverage gate stays satisfied and one sync remains **one undo step** labelled "Update birthdays from the
address book". Ctrl+Z then restores exactly what was printed before.

**UX.** On insert, if the roster holds birthdays in the issue month, one extra screen at the front of the
existing wizard: *"We found 7 birthdays in March in your address book. Add them?"* [Yes, add them] /
[No, I'll type them myself]. With an empty roster the wizard is exactly what it is today. Later, the
panel's Birthdays section offers "Bring in birthdays from the address book" (`Ctrl+Shift+U`, also on the
Insert menu), and when a generated list has gone stale, a caption: *"The address book has changed since
this list was made."* [Update it]. The re-sync dialog is a plain three-way diff — what will be added,
what will be taken away and why, and *"we'll leave alone: 2 lines you typed yourself"* — under
*"Nothing changes until you press Update the list."*

Hand edits survive re-sync. Provenance is captured in the existing setter lambdas
(`(r, v) => { r.Name = v; r.IsManual = true; }`), which works for both the step wizard and the grid
editor because both drive the same field bindings — one line per bound field, no framework change,
`WidgetGridWindow` untouched.

> **Hard rule: staleness never mutates the document.** Auto-applying on open would dirty a file the user
> opened only to look at, trip the 60-second autosave and grow the recovery snapshot. The nudge is a panel
> caption and a "What's next" row. Nothing more.

**Carry-forward stays completely untouched.** After a carry-forward, `sourceMonth` (3) no longer matches
`IssueMonth` (4), so the staleness check fires on its own and the "What's next" card leads with *"The
birthday list still shows March birthdays. Update it to April?"* — one click, one undo step, and the user
watches it happen. That beats a silent clear (which would destroy hand-typed rows) and beats today's
silent copy (which prints the wrong month). Teaching `CarryForward` to regenerate was considered and
rejected: `Core` is BCL-only and must not learn about the roster or widget internals.

**Officers — autocomplete, not generation.** A new `WizardFieldKind.Person`, rendered as an editable
AutoCompleteBox over roster names, with **free typing always allowed** — a name not in the book must
still be typeable, no dead ends. Candidate names are passed into the window by `App`, not pulled from an
ambient source, preserving §5's rule. Two cheap wins that hit the complaint directly: picking a person
**auto-fills a blank Phone**, and if the typed phone differs from the book's, the review screen offers one
checkbox — *"Also update A. Placeholder's phone in your address book?"*, keeping the book fresh without a
separate chore. The officers wizard stays 14 screens (a deliberate M7 design), but each screen becomes a
pick rather than typing a name and a phone number.

**Committees — a picker, no schema change.** Members stay `List<string>`. The multi-line field gains
"Add someone from the address book", appending the picked name as a line. No migration, no layouter
change, no risk.

**Deferred, with the reason stated: full officer-table generation.** The roster has an `office` field, so
this looks like a free win — but matching free-text office strings ("Sr. Warden", "SW") to the twelve
fixed standard positions is a real matching problem, and page 2's officers table is the single most
conspicuous place a wrong name can print. Ship the picker, let real office strings settle over a few
issues, then generate with the same diff-and-confirm pattern birthdays use. DistrictCalendar, EventCard
and CoverBanner get nothing — no people in them.

**Scope cut order if M13 runs long:** committee picker → officers phone auto-fill and write-back →
officers person picker → the re-sync diff dialog (degrade to "regenerate, one undo step, plain confirm").
**Never cut: birthday generation.**

**Testing:** projection is idempotent (apply twice = apply once); manual rows survive a sync;
`removedMemberIds` entries are never resurrected; stored order untouched (the layouter still sorts);
fingerprint changes iff a contributing field changes; a v1 payload migrates to v2 with `source: "manual"`;
`TemplateTests` re-runs unchanged as proof the privacy gate is undisturbed. Headless: insert with a
fictional roster loaded → accept → assert entries; hand-edit one row → re-sync → that row is untouched
and Ctrl+Z restores the prior payload byte-for-byte. Also add a test that **every `WizardFieldKind` value
renders a distinct control type in both `WizardWindow` and `WidgetGridWindow`** — a missed case silently
degrades to a plain TextBox.

**Rollout note:** `WidgetController.CanEdit` refuses to edit a widget whose `dataVersion` exceeds the
build's, so a v2 birthday list opened in a pre-M13 build becomes move/resize/delete-only with the
"newer version" message. That is designed behaviour, but users mid-Velopack-rollout will see it.

**Post-milestone:** `/graphify . --update`; ingest the M13 decisions into llm-wiki.
**Agents:** **Opus** for the projection and re-sync semantics; **Sonnet** for the wizard field kind and
pickers; **cavecrew-reviewer (Sonnet)**.

### Sizing & sequencing notes
- L milestones to watch: **M1, M4, M7** — each gets a Plan-agent spec pass and Fable/Opus implementation.
- Hard ordering: M1 before everything UI (retires the existential risk); M2 before M3; M4 before M5; M7 needs M4+M5; M8 needs M4; M9 needs M7+M8.
- Parallelizable: M6 (Imaging) is independent of M4/M5 and can run alongside; the six widgets inside M7 parallelize across agents.
- **Post-release additions (M11–M13):** hard ordering is **M13 needs both M11 and M12**. M11 and M12 are
  otherwise independent and parallelize cleanly across two agents — M11 touches `MainWindow` and
  `Editing`, M12 is an entirely new project plus its own dialogs.
- **Ship M11 first.** It fixes what the user hits every single time they open the app, needs no new data,
  no new dependency and no migration, and it builds the surface M13 hangs its buttons on. It is also the
  riskiest of the three (every menu item, the keyboard dispatcher, two constraining tests) — hence Opus
  and a full manual NVDA run before it is called done.

---

## 12. Verification (end-to-end)

1. **Per-milestone:** `dotnet build && dotnet test` locally + 3-OS CI matrix green; cavecrew-reviewer findings addressed; snapshot diffs reviewed as CI artifacts.
2. **Layout correctness:** golden LineBox tests + cross-OS byte-identical snapshots (determinism proof).
3. **WYSIWYG guarantee:** PDF-rasterize-vs-screen-snapshot parity test in CI (Linux `pdftoppm` job).
4. **The real-world test (final):** recreate one complete existing issue (July 2026) in the app from a template; export; committee member (the user is on the trestle board committee) compares against `Examples/July 2026.pdf` side by side.
5. **Accessibility gate (M9):** keyboard-only full-issue authoring run; NVDA/VoiceOver script pass; high-contrast theme visual audit.
6. **Install test (M10):** clean-machine installs on Windows + macOS using only the written instructions; auto-update round-trip via a test tag.
7. **Privacy gate (every milestone):** before pushing, confirm no example PDFs, real names, or phone numbers appear in `git log -p` for the new commits; templates/fixtures spot-checked for fictional data only; original image bytes verified byte-identical in saved `.tboard` containers after edits (guarantees future re-crop/resize). **From M12 this re-runs after every roster stage**, and additionally checks that no roster file or roster-derived fixture exists outside `tests/Roster.Tests` (§0 rule 5).
8. **Action-surface gate (M11):** keyboard-only run reaching and invoking every action offered in the panel; no `Blocked` availability carries an empty reason string; no action-panel control is ever greyed; manual NVDA pass at 200% UI scale in High Contrast, specifically validating the panel-heading live region and the greyed-with-`HelpText` menu behaviour.
9. **Roster gate (M12):** importing the same file twice changes nothing; export → edit in Excel → re-import moves only the edited fields; a phone-only import leaves every birthday intact; no roster file ships in the repo.
10. **Roster-to-widget gate (M13):** birthday re-sync is one undo step and Ctrl+Z restores the prior payload byte-for-byte; hand-edited rows survive a re-sync; deliberately removed members are not resurrected; `TemplateTests` passes unchanged.

## Flagged uncertainties (verify early, all covered by M1/M3 spikes)
- SkiaSharp 3.x `SKShaper`/HarfBuzzSharp packaging and PDF text-embedding behavior → M1.
- Avalonia `ISkiaSharpApiLeaseFeature` under heavy per-frame redraw → M3 (WriteableBitmap fallback ready).
- Skia PDF JPEG passthrough for recipe-adjusted images (adjusted images re-encode; check sizes) → M8.
- Avalonia Linux AT-SPI screen-reader completeness → M9 (best-effort; Windows/macOS are the tested SR targets).

### Flagged uncertainties for M11–M13 (added 2026-07-26)
- **Does "greyed menu + explained panel" actually read correctly under NVDA/VoiceOver?** The panel hides
  inapplicable actions and explains blocked ones while the menu keeps conventional greying. If the manual
  M11 pass shows the two surfaces disagreeing confusingly, the fallback is explained refusal in the menu
  too (always-enabled items that refuse with a spoken reason). **This is the design decision most in need
  of real-screen-reader validation before it is locked in.**
- **Chrome budget at 200% scale with the panel docked** → M11, verify empirically; mitigations are
  specified but the 40%-of-frame estimate is not measured.
- **ClosedXML's transitive footprint** (DocumentFormat.OpenXml + SixLabors.Fonts) against the
  ~90–130 MB self-contained budget × 4 RIDs → measure at M12 start, *before* the import UI is built. If
  it is unacceptable, the fallback is a minimal hand-rolled OOXML reader/writer over
  `System.IO.Compression` + `System.Xml` (we need roughly 2% of the format: a flat table in and out,
  writable with inline strings and no shared-string table).
- **Excel date serials** (`45123` rather than `7/4`) in a Birthday column are the most likely single
  import failure → fixture-test it at M12 before anything else in the reader.
- **The roster is single-machine, single-user.** Two committee members on two laptops diverge with no
  merge; XLSX export is the sharing path. Say so in the People window's help text, not only here. A later
  milestone could embed an optional roster copy in the `.tboard` — deliberately not now, since it
  re-couples app state and document state.
- **Mid-rollout `dataVersion` 2** (M13): a v2 birthday list opened in a pre-M13 build is
  move/resize/delete-only. Designed behaviour, but visible to users during a Velopack rollout.
