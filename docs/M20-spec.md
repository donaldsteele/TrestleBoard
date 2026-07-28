# M20 — The font window, taken apart and put back honestly

**Delivered 2026-07-28.** Implements PLAN.md §11 M20. This file records what was built, where it
differs from the plan, and what is still open.

M20 is **chrome only**. M14's engine semantics — `SetCharacterStyleFontCommand`, the `~` override
naming, the sibling sweep, the fixed size ladder — are untouched, and `TextStylesShellTests` (M14's
font gate, PLAN.md §12 item 11) re-runs **unchanged** as the proof. Not one snapshot baseline moved
and no layouter was opened.

What changed is that the window stopped throwing away what the user picked. The rule the whole
milestone mechanises:

> Nothing in this window discards a choice without either applying it or saying, in words, that it
> is not applying it. Cancel is the only path that changes nothing, and Cancel says so on the
> button.

---

## 1. The ten defects, and what closed each

The hands-on session of 2026-07-27 enumerated ten. Each row names the closing change and the test or
picture that holds it closed.

| | Defect | Closed by | Held by |
|---|---|---|---|
| (a) | Category headings were selectable, and selecting one silently discarded the pending choice | Heading rows are unselectable, unfocusable, un-hit-testable, and skipped in the direction of travel | `ACategoryHeadingCannotBeSelectedAndKeepsThePendingChoice` |
| (b) | Apply with nothing pending was a silent no-op | Apply says "Nothing to change yet — pick a different font or size first." | `ApplyWithNothingPendingSaysSoInsteadOfDoingNothing` |
| (c) | Switching roles discarded unapplied edits | Switching applies the pending edit first and names whose it was; closing the window applies it too | `SwitchingRolesAppliesThePendingChangeAndSaysWhose`, `ClosingTheWindowAppliesAPendingChangeRatherThanLosingIt`, `CancelIsTheOnlyPathThatChangesNothing` |
| (d) | "Just here" reused the whole-newsletter sheet with the wrong warning | `TextStylesMode.JustHere`: no role list, the selection's own words in the preview, a selection-scoped warning | `JustHereShowsNoOtherRolesAndOnlyTheSelectionScopedWarning`, `JustHereAppliesToTheHighlightedWordsAndCloses` |
| (e) | Fixed pixel heights clipped at large fonts | Every fixed `Height` gone; the lists take a `*` grid row and the window carries min/max bounds | `docs/images/font-picker.png`, and the 200 % pass in §5 |
| (f) | `ListBox` inside a `ScrollViewer` broke virtualization | The outer `ScrollViewer`s are gone and the rows are DATA (`FontRow`, `RoleEntry`) behind a `FuncDataTemplate`, not pre-built controls | the same picture — the selected family is scrolled into view, which a broken bring-into-view cannot do |
| (g) | Preview chips painted an opaque white plate, illegible on the selection highlight | Previews are rasterised on a **transparent** background; the foreground stays a parameter | `docs/images/font-picker.png` (the selected row reads), and the by-eye pass in §5 |
| (h) | Roles sorted by raw style name | `StyleLabels.DeclaredOrder` — the order a page is read in | `RolesCarryADeclaredSemanticOrderRatherThanTheAlphabetOfTheirRawNames`, `TheRolesAreListedInTheOrderAPageIsReadIn` |
| (i) | The label printed twice per row | The rendered-PNG copy of the role label went; the font sample line stayed | `BuildRoleRow`, and the picture |
| (j) | Search re-rendered every preview per keystroke | A 200 ms debounce **and** `FontPreviewCache` | `TheSecondAskForTheSamePreviewComesOutOfTheCache` |

---

## 2. The split (never-cut item 1)

One class, two modes, chosen at construction:

```csharp
public enum TextStylesMode { Newsletter, JustHere }
```

Two windows would have duplicated the font list, the size ladder, the preview and the pending-change
model — four things that must not drift apart — to separate two footers and a list. One class with a
mode keeps the shared machinery shared and makes the differences enumerable in one place:

| | `Newsletter` | `JustHere` |
|---|---|---|
| Title | "Fonts and text styles" | "Use a different font just here" |
| Role list | every base role | **none** |
| Preview words | the newsletter's first words | **the highlighted words** (`TextEditorController.SelectedText`) |
| Warning | "…every piece of writing that uses Body text … a different number of pages" | "This changes only the writing you have selected." |
| Whole-newsletter override footer ("Show me" / "Put them all back") | shown | **hidden** — it was never about these words |
| Apply | applies and **stays open** | applies and closes: one selection, one answer |

`MainWindow.UseFontJustHereAsync` no longer builds the sheet with its title swapped. The one new
public member outside the App project is `TextEditorController.SelectedText`, a read-only accessor
over machinery that already existed (`StoryNavigator.GetRangeText`) — no command, no model change.

---

## 3. The pending-change model (never-cut item 2)

`RoleEntry` carries a per-role **baseline** (family, size) that starts at the style sheet's values
and moves only when Apply applies. "Pending" is derived, never stored: the selected family or the
stepped size differs from the baseline. That derivation is why the model survives the caller not
wiring anything up — the window does not depend on the document mutating underneath it.

Four exits, four behaviours, none of them silent:

- **Apply** — applies, updates the baseline, refreshes the role row, says what it did, and (in
  `Newsletter`) stays open so a second role costs no second trip. Each Apply is its own
  `IDocumentCommand` and its own undo step, in the order pressed.
- **Switching roles with an edit pending** — applies it first, then says: *"Your change to Body text
  was applied before moving on — nothing was lost. You are now changing Photo captions."* The plan
  allowed "applies or asks"; **applies** was chosen because a modal question inside a modal window
  is exactly the interruption an elderly user reads as an error.
- **Closing the window (the title-bar X)** — applies. The X says nothing about discarding, so it
  must not mean it. This is `OnClosing`, so Alt+F4 and the window-menu Close go the same way.
- **Cancel** — the one path that changes nothing, and the button now says so:
  **"Cancel — change nothing"**. Escape reaches it through `IsCancel`.

"Show me" and "Put them all back" set the same suppression flag as Cancel: both are answers to the
override footer's question, not to the font list, and neither should smuggle a font change out with
it.

---

## 4. Headings that are not choices (never-cut item 3)

Three mechanisms, because a row is reached three different ways:

1. **The container** — `ContainerPrepared` switches `IsEnabled`, `Focusable` and `IsHitTestVisible`
   off for every non-family row. That covers the mouse and the tab order.
2. **The selection guard** — if selection lands on a heading anyway, it steps to the next family
   **in the direction of travel** (and the other way if the list ends there). That is what makes an
   arrow key skip a heading rather than stop on it.
3. **The automation name** — the category text is not lost by making the heading unreachable: every
   family row's name is `"{family}. {category}. {description}"`, so a screen reader hears
   *"Lora. Fonts with little feet on the letters. A serif with brushed, calligraphic edges…"*.

That third point is M14 §13's grouped-list question answered **by test**
(`EveryHeadingsWordsSurviveOnTheFamilyRowsAutomationName`) rather than by an `AutomationPeer`. The
NVDA confirmation is still a manual item — see §5.

The **unbundled-family row** uses the same machinery: a third row kind, `Unavailable`, unselectable
like a heading, carrying M14's language ("…this copy of TrestleBoard does not have it… the
newsletter itself is not changed, and it will save back exactly as it was"). Before M20 a document
naming a family this build does not bundle simply had nothing selected in the list, which reads as a
list that has lost your font.

---

## 5. Layout, previews and the cache

- **No fixed heights.** The role list and the font list each take a `*` row in their own grid and
  own their scrolling. The window gets `MinWidth`/`MinHeight`/`MaxWidth` instead of a hard-coded
  520-pixel viewport, so 200 % UI scale grows the lists rather than clipping them.
- **Rows are data.** `FontRow` and `RoleEntry` behind `FuncDataTemplate`s, so the `ListBox`
  virtualizes and brings the selected item into view. The previous design put pre-built `StackPanel`s
  in `Items`, which realises every row whether or not it is on screen.
- **Previews composite honestly.** Background `0x00000000`. The foreground is still a parameter
  (`darkTheme`), never read from a static — M14's rule, kept.
- **Previews shrink rather than crop.** An Avalonia `Image` draws an unstretched bitmap *centred* in
  its slot whatever its alignment says, so a preview wider than the column lost a letter off each
  end — "Brethren" read as "rethren", which is worse than useless in a window about legibility.
  `Stretch.Uniform` with `StretchDirection.DownOnly` keeps the 1.5× rasterisation crisp at every
  size that fits and scales only the ones that do not.
- **`FontPreviewCache`** keys on (family, size, text, foreground, background). Weight and slant are
  absent because this window only ever previews the Regular face; colours are present because a
  window opened in the dark theme must not be handed the light theme's bitmaps. The bitmaps are
  **not** disposed when the window closes: a render pass can still be queued against the rows it
  drew, and a disposed `Bitmap` under the compositor is a crash. The cache is bounded by the bundled
  catalogue, so the collector is the right owner.
- **Search is debounced** at 200 ms. The debounce and the cache are separate wins: the debounce
  stops the rebuild, the cache stops the re-rasterisation when the rebuild happens anyway.

---

## 6. What the plan asked for and did not get

Nothing was cut. The scope-cut order (preview caching → the unbundled-family row → the role
reorder) was never reached; all three shipped.

One deliberate divergence: the plan offered "two windows, **or** one window in two explicit modes
with different titles and footers", and this took the second option, for the reason in §2.

---

## 7. Still open, and owned by a person

- **A manual NVDA pass over the grouped font list** — that the category is heard on each row, that
  the arrow keys never land on a heading, and that the pending-change messages are announced (they
  are `AutomationLiveSetting.Polite` live regions). The test proves the automation name carries the
  category; only a person with a screen reader can prove it is *heard*. `docs/accessibility-test-
  script.md` is where it belongs; PLAN.md §13 carries it.
- **A by-eye pass at 200 % in all three themes.** The fixed heights are gone and the previews are
  transparent, which is what the defect asked for; whether the result *looks* right at 200 % in High
  Contrast is a judgement a machine cannot make. It folds into M16's existing by-eye item rather
  than adding a second one.

---

## 8. Files

| File | What |
|---|---|
| `src/TrestleBoard.App/Dialogs/TextStylesWindow.cs` | rewritten: two modes, pending-change model, data rows, unselectable headings |
| `src/TrestleBoard.App/Dialogs/FontPreviewCache.cs` | new: rasterised lines, keyed and shared |
| `src/TrestleBoard.App/MainWindow.axaml.cs` | `Applied` wiring; "just here" builds the picker in its own mode |
| `src/TrestleBoard.Core/Text/StyleLabels.cs` | `DeclaredOrder`, `OrderOf` |
| `src/TrestleBoard.Editing/TextEditorController.cs` | `SelectedText` (read-only) |
| `tests/App.HeadlessTests/TextStylesWindowTests.cs` | new: M20's gate, 13 tests |
| `tests/Core.Tests/StyleFontTests.cs` | the declared order |
| `docs/images/font-picker.png` | re-baked |
