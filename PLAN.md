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
| MVVM | **CommunityToolkit.Mvvm 8.4+** | Source-generated observables/commands; `WeakReferenceMessenger` for cross-panel events. |
| Serialization | **System.Text.Json** (source-generated) | camelCase, UTF-8, unknown-property preservation for forward compatibility. |
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

---

## 6. Accessibility (first-class, elderly users)

- **Screen readers:** `AutomationProperties` on every control; custom `AutomationPeer`s expose the canvas block tree ("Officers table", "Photo: <altText>", text content). Tested targets: Windows Narrator/NVDA + macOS VoiceOver; Linux AT-SPI is best-effort (weakest Avalonia leg — verify empirically in M9).
- **Keyboard-only operation everywhere:** every command has a menu item + shortcut; Tab cycles blocks, arrows nudge (Shift=10pt), Enter enters text edit, Esc exits. Drag-and-drop is ALWAYS an accelerator, never the only path (e.g., "Insert photo…" dialog beside drag-drop).
- **Scale:** minimum UI font 16pt (wizards/dialogs 18–20pt); app-wide UI scale 100–200%; canvas zoom 50–400% independent, "Fit page" default.
- **Themes:** Light (default), Dark, true High Contrast (7:1+); respect OS hint.
- **Hit targets:** ≥44×44px; oversized canvas selection handles (12pt visual/24pt hit).
- **Forgiveness:** unlimited undo; plain-language confirms ("Delete this photo? You can Undo this."); no icon-only buttons (icon+text); autosave per §4.
- Wizards are the a11y centerpiece: linear, one question at a time, no dragging required for any data entry.

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
  TrestleBoard.Widgets       IWidgetDefinition, registry, 6 built-ins.     Deps: Layout, Core.
  TrestleBoard.Export.Pdf    SKDocument export, metadata.                  Deps: Rendering.
  TrestleBoard.App           Avalonia shell, PageCanvasControl, wizard host, start screen, themes,
                             a11y peers, settings.  ONLY project referencing Avalonia.
 tests/
  Core.Tests / Layout.Tests / Imaging.Tests / Widgets.Tests
  Rendering.SnapshotTests (Baselines/ committed)
  App.HeadlessTests
 assets-src/                 OFL fonts, template sources, fixture images
 .github/workflows/          ci.yml, release.yml
```
Dependency rule: `Core` references nothing; layout/render stack runs headless (enables PDF export, snapshot CI, and a free CLI batch-export bonus).

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

### M10 — Packaging & release (S/M)
**Goal:** installable by an 80-year-old.
**Deliverables:** Velopack packaging for 4 RIDs; GH Actions release workflow on tag; auto-update wired to GitHub Releases; `.tboard` file association; plain-language install instructions with SmartScreen/Gatekeeper screenshots (no code signing — documented workarounds).
**Acceptance:** on fresh Windows and macOS machines: download → install → open template → export PDF following only the written instructions; pushing a new tag produces an update an installed copy picks up automatically.
**Post-milestone:** `/graphify . --update` and `/graphify . --wiki` (architecture wiki for future maintenance); ingest the M9 AND M10 decisions into llm-wiki; `/wiki:lint`. **Carried over from M9** — M9's post-milestone plugin pass was deliberately deferred here rather than run at the time (owner's call, 2026-07-26), so this step covers both milestones.
**Agents:** **Sonnet** throughout; `claude-code-guide` (Sonnet) for Velopack/Actions specifics; **cavecrew-reviewer (Sonnet)**.

### Sizing & sequencing notes
- L milestones to watch: **M1, M4, M7** — each gets a Plan-agent spec pass and Fable/Opus implementation.
- Hard ordering: M1 before everything UI (retires the existential risk); M2 before M3; M4 before M5; M7 needs M4+M5; M8 needs M4; M9 needs M7+M8.
- Parallelizable: M6 (Imaging) is independent of M4/M5 and can run alongside; the six widgets inside M7 parallelize across agents.

---

## 12. Verification (end-to-end)

1. **Per-milestone:** `dotnet build && dotnet test` locally + 3-OS CI matrix green; cavecrew-reviewer findings addressed; snapshot diffs reviewed as CI artifacts.
2. **Layout correctness:** golden LineBox tests + cross-OS byte-identical snapshots (determinism proof).
3. **WYSIWYG guarantee:** PDF-rasterize-vs-screen-snapshot parity test in CI (Linux `pdftoppm` job).
4. **The real-world test (final):** recreate one complete existing issue (July 2026) in the app from a template; export; committee member (the user is on the trestle board committee) compares against `Examples/July 2026.pdf` side by side.
5. **Accessibility gate (M9):** keyboard-only full-issue authoring run; NVDA/VoiceOver script pass; high-contrast theme visual audit.
6. **Install test (M10):** clean-machine installs on Windows + macOS using only the written instructions; auto-update round-trip via a test tag.
7. **Privacy gate (every milestone):** before pushing, confirm no example PDFs, real names, or phone numbers appear in `git log -p` for the new commits; templates/fixtures spot-checked for fictional data only; original image bytes verified byte-identical in saved `.tboard` containers after edits (guarantees future re-crop/resize).

## Flagged uncertainties (verify early, all covered by M1/M3 spikes)
- SkiaSharp 3.x `SKShaper`/HarfBuzzSharp packaging and PDF text-embedding behavior → M1.
- Avalonia `ISkiaSharpApiLeaseFeature` under heavy per-frame redraw → M3 (WriteableBitmap fallback ready).
- Skia PDF JPEG passthrough for recipe-adjusted images (adjusted images re-encode; check sizes) → M8.
- Avalonia Linux AT-SPI screen-reader completeness → M9 (best-effort; Windows/macOS are the tested SR targets).
