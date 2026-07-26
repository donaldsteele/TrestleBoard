# TrestleBoard

Cross-platform desktop editor (Windows/Linux/macOS, .NET 10 + Avalonia) that lets the
Indian Land Masonic Lodge 414 trestle board committee — primarily elderly users — produce
their monthly 4–6 page newsletter and export a distribution-ready PDF.

**The master plan is `PLAN.md`. It is approved and locked — follow it; do not re-design.**
Current milestone status is tracked in git history; check the latest commits.

## ⚠ Privacy rules (HARD requirements — PLAN.md §0)

The `Examples/*.pdf` files contain **real people's names, phone numbers, and emails**.

1. `Examples/`, `wiki/`, `raw/`, and `graphify-out/` are gitignored and must NEVER be
   committed, pushed, or copied into tracked paths. Verify with
   `git check-ignore Examples/ wiki/ raw/ graphify-out/` before any push.
2. No real personal data in templates, fixtures, tests, or snapshots — fictional
   placeholders only (e.g., "A. Placeholder, Worshipful Master, 555-0100").
3. llm-wiki (`wiki/`, `raw/`) and graphify (`graphify-out/`) outputs absorb real data from
   the example PDFs — they are local development aids, never repo artifacts.
4. Before pushing: confirm `git log -p` for new commits contains no real names/phones.

## Build & test

```
dotnet build TrestleBoard.slnx            # requires .NET 10 SDK (pinned in global.json)
dotnet test TrestleBoard.slnx
dotnet run --project src/TrestleBoard.App
```

CI: `.github/workflows/ci.yml` — build + test on windows/ubuntu/macos-latest. All three
must be green; snapshot determinism across OSes is a core guarantee.

## Architecture (summary — details in PLAN.md §1–§9)

- The app owns its layout + rendering pipeline on SkiaSharp/HarfBuzzSharp; the same
  renderer draws the editor canvas and the PDF (`SKDocument`) — WYSIWYG by construction.
- Avalonia is chrome only: **`TrestleBoard.App` is the ONLY project referencing Avalonia.**
- `TrestleBoard.Core` references BCL only. Dependency flow:
  Core ← Layout ← Rendering ← Export.Pdf; Core+Layout ← Widgets; Imaging standalone.
- Documents are `.tboard` zip containers (manifest/document/styles JSON + original image
  assets); all mutations go through `IDocumentCommand` (full undo/redo).
- Package versions are centrally managed in `Directory.Packages.props`; SDK pinned in
  `global.json`. Avalonia stays on the 11.3.x line (plan-locked).
- Accessibility is first-class (elderly users): 16pt+ UI fonts, full keyboard paths,
  screen-reader peers, plain-language dialogs. See PLAN.md §6 before touching UI.

## Mandatory knowledge tooling (PLAN.md §10)

- **llm-wiki**: domain knowledge base in `wiki/` (gitignored). Query with `/wiki:query`
  (e.g., "what sections does a cover page contain?"). After each milestone, ingest that
  milestone's design decisions. `/wiki:lint` after even-numbered milestones.
- **graphify**: run `/graphify . --update` after every milestone from M2 on; answer
  architecture questions via `/graphify query` once `graphify-out/` exists.
