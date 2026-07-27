# M15 — Screenshots & README

**Delivered 2026-07-27.** Implements PLAN.md §11 M15, the final milestone. This file records
what was built, the decisions that differ from the plan, and what is still open.

Before M15 the project's front page was 34 lines of prose with no images, describing an
application whose entire argument is visual: text that flows around a photograph, a panel that
explains itself, a typeface you can see before you commit to it.

---

## 1. The spike, first — does headless Skia expose the lease?

The plan named this the single biggest unverified assumption in the milestone, and it was task
one. `PageDrawOperation.Render` returns early when `ISkiaSharpApiLeaseFeature` is absent, which
would have made every window screenshot a 1280×860 picture of grey backdrop where the newsletter
should be.

**It is present.** With

```csharp
AppBuilder.Configure<App>()
    .UseSkia()
    .WithInterFont()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
```

the compositor renders through Skia, the lease is handed over, and the page area is drawn by the
same engine that draws the PDF. The `DocumentRenderSource.RenderPageToPng` composite fallback was
written into the plan as insurance and **was not needed**; it is not in the harness, because dead
code kept "just in case" is a maintenance cost with no reader.

Verified visually on the first run: the hero shot shows shaped Source Serif 4 body text breaking
cleanly around the cover photograph, which is exactly the output the lease-absent path could not
have produced.

## 2. The harness

`tools/TrestleBoard.Screenshots` — a console exe referencing `TrestleBoard.App` and
`Avalonia.Headless`, with an `InternalsVisibleTo` entry beside the existing one. It is in
`TrestleBoard.slnx` so CI compiles it and it cannot rot; CI never runs it. It is not shipped, and
is the second of exactly two non-shipped projects allowed to reference Avalonia (PLAN.md §9).

| File | What it is |
| --- | --- |
| `Program.cs` | Argument parsing, the run loop, the summary line |
| `ScreenshotHarness.cs` | The Avalonia session, the privacy redirect, and `Stage` — everything a shot is allowed to do |
| `ShotList.cs` | The eighteen shots, as data |
| `Shot.cs` | Name, kind, milestone, caption, alt text, and the delegate that takes it |
| `MilestoneGate.cs` | Skip-with-a-reason when a milestone is not in the build |
| `Fixtures.cs` | The fictional ten-person address book for the People window |
| `PageSpread.cs` | The three-up PDF render — no Avalonia involved |
| `Png.cs` | Text-chunk stripping |
| `ImageIndex.cs` | Generates `docs/images/README.md` |

Both rejected alternatives in the plan stayed rejected. Flipping `UseHeadlessDrawing` in
`App.HeadlessTests` would change the drawing path for every existing test file, because it is a
per-process global with one session per process. A `--screenshot` flag in the shipped app would
risk colliding with the switches Velopack passes through argv, which `StartupOptions` ignores by
design.

**Usage.** `dotnet run --project tools/TrestleBoard.Screenshots`, with `--only a,b`, `--out <dir>`,
`--list` and `--help`.

## 3. Determinism

Four things are pinned, three of them non-obvious:

- **Settings are injected, never loaded.** `Stage.DocumentationSettings` is Light, 100%, panel
  shown. The maintainer's own theme cannot change what the documentation looks like.
- **`.WithInterFont()`**, whose omission would silently make every shot machine-dependent — the
  chrome would fall back to whatever the machine happened to offer.
- **`RestoreDialog`'s clock.** Its constructor already takes the "now" value, so the shot passes a
  fixed one alongside a fixed `SavedAt`; the dialog phrases the age of the snapshot in words
  ("saved 2 minutes ago"), which a real clock would change on every regeneration.
- **The caret-blink timer** is avoided rather than forced: no shot enters text-edit mode, which is
  the only state in which the timer runs.

Window size is the app's own 1280×860 default. Dialogs keep their own sizes unless the shot passes
one — a screenshot that silently resized a window would document a layout the user never sees.
Three do pass one, and each is noted in the source: the font sheet and the People window size
themselves to an owner they do not have offscreen, and the settings dialog is `SizeToContent.Height`.

## 4. The privacy hazard, and the three layers

M12 put real names in `%AppData%`, and this harness runs on the maintainer's own machine by design.
A screenshot of the People window with the real roster loaded would be the most direct possible
violation of PLAN.md §0.

1. **Structural — the only layer that actually works.** `AppPaths.Root` is redirected to a
   temporary folder *before Avalonia starts*, so the process cannot read
   `%AppData%/TrestleBoard/roster.json` at all. The People window shot runs against a fictional
   ten-person book written into that temporary folder; the import shot runs against the fictional
   hundred-person CSV, **linked** from `tests/Roster.Tests/Fixtures` rather than copied, because
   §0 rule 5 puts roster fixtures in exactly one place. The `AppPaths` settable root itself landed
   in M12, with the milestone that *creates* the hazard, exactly as the plan required.
2. **Tests.** `DocsTests.NoCommittedImageCarriesTextChunks` and
   `DocsTests.TheScreenshotHarnessUsesFictionalNamesOnly`.
3. **The rule.** §0 rule 6, and the CLAUDE.md restatement of it.

**Text chunks.** `Png.Sanitise` walks the chunk stream and drops every `tEXt`, `iTXt` and `zTXt`
record before the bytes reach disk. This is not theoretical: encoders stamp software names and
**file paths** there, and a path on this machine bears the maintainer's account name. It runs on
every image the tool writes, including the page spread, whatever the encoder did or did not do.

## 5. The shot list — eighteen images, 1.4 MB

Declared as data with the milestone each entry needs, so a slipping milestone degrades the
documentation rather than blocking it (PLAN.md §11, sizing notes). `MilestoneGate` resolves the
milestone's defining type **by name** rather than by reference: a direct reference would turn a
missing milestone into a compile error, which is precisely the blocking behaviour the mechanism
exists to avoid.

| Shot | Kind | Needs |
| --- | --- | --- |
| `hero-issue-page1` | window | — |
| `text-wrap-around-photo` | window | — |
| `action-panel-photo` | window | M11 |
| `action-panel-whats-next` | window | M11 |
| `officers-table-widget` | window | — |
| `font-picker` | dialog | M14 |
| `high-contrast` | window | — |
| `scale-200` | window | — |
| `start-screen` | dialog | — |
| `wizard-officers-step` | dialog | — |
| `wizard-review` | dialog | — |
| `grid-editor` | dialog | — |
| `fix-photo` | dialog | — |
| `settings` | dialog | — |
| `restore-dialog` | dialog | — |
| `people-window` | dialog | M12 |
| `import-columns` | dialog | M12 |
| `pdf-page-spread` | render | — |

Two shots are taken in a deliberately chosen state rather than the default one, because the default
would document nothing:

- **`action-panel-whats-next`** is taken *after* a carry-forward. With a freshly opened issue the
  card is nearly empty; after a carry-forward it says what the plan promised it would say — the
  articles still hold their reminder text, the cover date has moved to August, and the status bar
  is explaining what just happened.
- **`people-window`** has a person selected. With nobody chosen the form on the right is seven
  empty boxes, which documents the layout but not the point of it.

**`pdf-page-spread` — one deviation.** The plan asked for a three-up composite at 150 dpi. Three
US-Letter pages at 150 dpi is 3825px wide, and the plan's own "capture at 1×" rule points out that
GitHub's README column is about 880px. Both are honoured by doing them in order: the pages are
rasterised at 150 dpi, composited, and the finished strip resampled once to 1360px with a Mitchell
filter. Drawing the pages small in the first place would ask Skia to hint serif text at eight
points, which bands; this way the glyphs are drawn properly and then averaged down.

**Total: 1.4 MB**, against the plan's estimate of 4–5 MB. The plan's warning that this would
roughly double `.git` still held, for a smaller reason than expected: `.git` went from about 6 MB
to about 12 MB, most of it loose objects that will pack down. The warning behind the number is the
part that matters and is unchanged — **PNGs do not delta, so every regeneration adds a full copy to
history.** Regenerate rarely and by name. `pngquant` at 256 colours remains the lever if 4 MB ever
becomes a problem, and remains noted as something that must stay off the page and PDF renders,
where anti-aliased serif text bands.

## 6. `OpenIssueSample()` is wired up

Help → **"Show me an example newsletter"** (`help.exampleIssue`). A new `ActionId`, a catalog entry,
an always-available rule, and an `ActionRunner` handler — the four things M11 requires of a new
command. No shortcut, so no `KeyboardMap` row. It sits above Fonts and licences with its own access
key, which the M11 access-key test checks.

This is a genuine feature for this audience, not merely a screenshot convenience: the five-page
sample issue is the richest fixture in the repository and was reachable from no menu item at all.

## 7. Gating the plumbing, not the pixels

`tests/Core.Tests/DocsTests` — pure file and regular-expression work, no Avalonia and no Skia, so it
runs on all three operating systems:

| Test | What it catches |
| --- | --- |
| `EveryReferencedImageExists` | A broken image on the GitHub front page |
| `EveryCommittedImageIsReferenced` | Dead weight — PNGs never delta |
| `EveryImageReferenceCarriesRealAltText` | "screenshot" passed off as alt text |
| `NoCommittedImageCarriesTextChunks` | A file path, and an account name, inside a PNG |
| `TheScreenshotHarnessUsesFictionalNamesOnly` | A real name reaching a fixture |
| `TheReservedInstallShotsAreNamedButNotLinked` | Someone closing M10's open items by writing a link |

The screenshots themselves are **not** a test. Gating them would either block every unrelated UI
tweak on a re-bake, or need a tolerance, at which point they stop being a gate at all. `--only` is
the churn control instead.

## 8. The README

~177 lines, structured as questions a user would ask rather than feature names. The two opening
paragraphs are kept verbatim. No emoji and no badges — the file had none and badges are marketing
furniture.

Hero → **What it does** (lay out the page / write and format text / fill in the recurring lists /
photographs / your address book / export the PDF) → **Built for the people who use it** →
**Each month** → **Installing** (unchanged) → **How it works** (the three existing bullets plus a
fourth on bundled-font determinism) → **Building** → **Licence**.

**The licence section, and one thing it does not do.** The plan asked it to state "the code
licence, plus the bundled fonts' OFL/Apache terms". The fonts are straightforward: all twenty
families are SIL Open Font License 1.1 (there is no Apache family in the shipped set, despite §1's
wording), credited in `docs/FONTS.md`, with the full text inside the installer and reachable from
Help → Fonts and licences. **The application's own source has no licence file, and this milestone
did not invent one** — choosing a licence is the owner's decision, not a documentation task. The
README says so plainly instead, which makes the gap visible rather than merely absent. See §9.

## 9. Open items

- [!] **The application has no licence file.** M15 documents the absence rather than resolving it;
  picking a licence is the owner's call. Until one exists the README states that no permission to
  copy, modify or redistribute the code is granted.
- [!] **The two install screenshots stay open, and M15 does not close them.** `install-smartscreen`
  and `install-gatekeeper` are operating-system security dialogs that need a fresh Windows machine
  and a real Mac. They are **reserved** in `docs/images/README.md`, marked "hand-taken, not yet
  captured", with no `![…]` line — `DocsTests` fails on a referenced image that does not exist, and
  `TheReservedInstallShotsAreNamedButNotLinked` fails if somebody adds one early. M10 keeps its
  "two acceptance items stay open" wording. *Rendering imitation SmartScreen or Gatekeeper dialogs
  was rejected outright: a fabricated operating-system security dialog in install instructions for
  elderly users is actively harmful.*
- The shots show **Windows chrome**, deliberately: this audience installs on Windows
  overwhelmingly, and mixing operating-system chrome across one README looks broken.
- Two cosmetic things the shots made visible in the app itself, neither in this milestone's scope
  when it shipped — **both fixed 2026-07-27**, and the four affected images regenerated with
  `--only`:
  - The action panel clipped long button labels with their shortcut ("Wrap text around this
    (Ctrl+Shift+W)" ran off the 320px panel). A button's default content presenter does not wrap,
    so panel buttons now carry a wrapping `TextBlock` instead of a bare string, built by one
    `ActionPanel.PanelButton` factory. `ActionPanel.LabelOf` reads the label back for the audit
    tests, which previously cast `Content` to `string`. A half-readable shortcut is worse than
    none — the user cannot tell which key it ends in.
  - `WidgetGridWindow`'s docked button bar appeared to overlap the last visible row. The layout was
    right — a `DockPanel` with the `ScrollViewer` filling — but the bar had no background, so the
    row the scroller clipped read as being sat on. The bar is now a `Border` carrying
    `SystemControlBackgroundChromeMediumLowBrush` by reference, matching the toolbar and status
    bar, so the clip reads as a footer and stays correct in High Contrast.
