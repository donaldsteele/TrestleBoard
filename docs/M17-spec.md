# M17 — Click and type: the canvas answers the first click, and the menus reorganise

**Delivered 2026-07-27.** Implements PLAN.md §11 M17. This file records what was built, where it
differs from the plan, and what is still open.

M17 came out of the owner's first hands-on session with the app on 2026-07-27. Every complaint in
it was real. Almost none of them was a missing feature.

**Inline text editing has worked since M4.** Click into a text frame, type, and the words go in:
`PageCanvasControl.OnPointerPressed` → `TextEditorController.TryBeginAt` → `OnTextInput` →
`InsertText` → `InsertTextCommand`. `insert.photo` has worked since M6 and had *three* entry paths.
The overset badge has hung on the outflow corner since M5. What was missing, in every case, was the
**telling**: nothing on screen said that an empty box was for typing in, the picture command hid at
*This item ▸ Picture ▸ Insert a picture…*, and a red square with a plus in it is a symbol you have
to already know.

So M17 is a milestone about affordances, and its one structural rule is that it may not change what
anything *does*. The mechanical proof is that **no snapshot baseline moved** — `Rendering.SnapshotTests`
is unchanged at 156 passing, 12 skipped.

---

## 1. Adornments, and why they are drawn with Avalonia rather than Skia

The empty-frame hint and the hover outline are painted in `PageCanvasControl.DrawAdornments`, using
Avalonia's own `DrawingContext` — `DrawText`, `DrawRectangle`, the interface's font — immediately
after the custom Skia draw operation returns.

**Every other overlay in this control is Skia**, drawn inside `PageDrawOperation` through
`TextOverlayRenderer` and `FrameOverlayRenderer`, both of which live in `TrestleBoard.Rendering`.
Those two are already editor-only by discipline: `DocumentPdfExporter` calls `RenderPage` and
nothing else, so they are never reached on the export path. That discipline has held for eleven
milestones, but it *is* discipline — the classes sit in the assembly the exporter references, and
one call added in the wrong place would print an editor mark onto somebody's newsletter.

The adornments are not reachable that way at all. There is no path from an Avalonia `DrawingContext`
to `SKDocument`; the type system says so. That is why `DocumentRenderSource` gained a *query*
(`GetEmptyTextFrameRects`) rather than a drawing method.

The two colours come from the palette like everything else since M16 —
`TrestleBoard.Adornment.Hint` and `TrestleBoard.Adornment.Hover`, resolved through
`Tokens`/`TryFindResource` on every paint so a theme change repaints them.

**They are identical in Light and Dark, and that is deliberate.** They are painted *on the sheet*,
and `Page.Sheet` is `#FFFFFF` in all three variants because the user is laying out something that
will be printed on paper (docs/M9-spec.md §4). A "dark" hint would be pale grey on white. High
Contrast still moves, to `#000000`, because there the *floor* moves: the palette's CONTRAST block
gained two pairs measured against `Page.Sheet` (5.33 and 4.91 in Light and Dark, 21.00 in High
Contrast), and `ThemeCompositionTests` recomputes both from the hex values.

**`BrushFor` has no literal fallback.** If a token failed to resolve, the adornment is simply not
drawn. A hard-coded colour there is the exact defect M16 spent a milestone removing, and
`EveryTokenResolvesInAllThreeVariants` already fails the build if a key goes missing from a variant.

## 2. Making a text frame means you wanted to type

`MainWindow.AddTextFrame` now focuses the canvas and calls
`PageCanvasControl.BeginTextEditingOnSelection` — the same method Enter and F2 have always used. The
caret is in the new frame and the next keystroke is text.

This is a visible behaviour change and it broke three headless tests, all of which asserted that
`Ctrl+Shift+T` leaves a *frame selection*. It now leaves a *text session*, and the two are exclusive
modes (docs/M5-spec.md §1.2). Each test gained an `Escape` — which is exactly the step a user has to
take too, and the documented route from typing back to frame manipulation.

## 3. The widgets answer the double-click

Inline widget editing stays deferred; the owner confirmed the M7 ruling. What changed is that a
widget stops behaving like a hole in the page.

- **Double-click** on a widget → `PageCanvasControl.TryActivateWidgetAt` selects it and raises
  `WidgetActivated`; the shell runs `item.edit` **through `ActionRunner`**, so a widget made by a
  newer TrestleBoard refuses in plain language instead of opening a wrong dialog.
- **Single click** → the panel gains a caption from `ActionCatalog.DescribeSelectionHint`: *"This is
  a filled-in list. Use 'Change what this says…' to edit it — or just double-click it on the page."*
  A polite live region, so a screen-reader user hears it on the selection change.

**Widgets only.** A double-click on a photograph is left alone: there is no one obvious thing it
should do, and guessing wrong is worse than doing nothing. `DescribeSelectionHint` returns null for
everything else, including a widget this build cannot edit — that case already explains itself in
its refusal, and a second sentence telling the user to edit it would contradict the first.

## 4. Pointer honesty, and two fixes that rode along

- **The cursor is cached per shape.** `UpdateCursor` runs on every pointer move and used to
  construct a fresh `Cursor` — a native handle — each time. It now compares against the current
  shape first and looks the object up in a static dictionary.
- **A drag clamps at the page edge.** `OnPointerMoved` used to `return` when the pointer left the
  sheet, so the frame froze mid-drag and then jumped when the pointer came back — while it was
  happening, indistinguishable from the app having hung. `ToClampedPagePoint` holds the point inside
  the page instead.
- **A faint hover outline** marks the frame under the pointer, unless it is already the selection.

## 5. The menu restructure

**"This item" is dissolved.** It was named after a model concept, and its three submenus put four
commands three levels deep. Nine top-level menus before, nine after — the chrome budget at 200% UI
scale is the reason a tenth was not an option.

| Command | Was | Now |
|---|---|---|
| Add a text frame, Insert a picture… | This item (picture one level deeper still) | **Insert**, above the separator |
| Delete this, Change what this says…, Edit the list… | This item | **Edit**, after Select all |
| Fix this picture, Adjust the picture… | This item ▸ Picture | **Format ▸ Picture** |
| Wrap / Continue / Stop continuing / Make the rest fit, Fit to contents, and the four stacking commands | This item and its submenus | **Arrange** (new top level) |
| Move to the previous part of the window | *nowhere* | **View** |

*Rejected: a top-level Picture menu.* Ten top-level menus at 200% for two items.

**A slot is reserved in Insert** for M19's "bring in the officers from the address book", directly
under "Lodge officers" exactly as the birthday sync sits under "Birthdays". M19 adds one item rather
than restructuring the bar again.

`view.previousRegion` was, until this milestone, the only `ActionId` in the application with no menu
item anywhere. Shift+F6 worked; nobody could have found it.

## 6. The tests that make the restructure safe

`tests/App.HeadlessTests/MenuIndexTests.cs`:

- **`EveryActionInTheCatalogHasAMenuItem`** — every `ActionCatalog` entry has at least one menu item
  whose `Tag` matches. Its exception list `MayLiveOutsideTheMenus` **is empty and a second assertion
  requires it to stay empty**. PLAN.md §6 has promised this since M0; nothing checked it, which is
  how Shift+F6 went five milestones without a home.
- **`EveryMenuGestureIsTheOneTheKeyboardTableDispatches`** — every menu `InputGesture` matches a
  `KeyboardMap` row for that action.

  **Deviation from the plan.** The plan asked that the gesture *string* equal `KeyboardMap.Describe`.
  It cannot: XAML spells a key `OemCloseBrackets` and `Describe` spells the same key `]`, because
  one is written for a parser and the other for a person. The test compares the parsed
  `KeyGesture`'s `Key` and `KeyModifiers` instead, which is what the two actually have to agree
  about; comparing the texts would only have proved they are written for different readers.
- **`TheMenuBarStaysNineWide`** — the nine headers, in order.

## 7. Overset gets its sentence

The marker was already there. What it lacked was words, so both status sentences now name the
command and its shortcut:

- nothing selected — *"There is more writing than fits — 'Make the rest fit' (Ctrl+Shift+M) will
  flow it."* (`MainWindow.UpdateStatus`, now reading `ActionContext.HasOversetText`, which
  `ActionContextFactory` already computed and nothing surfaced)
- the overflowing frame selected — the same sentence plus *"or make this frame bigger"*
  (`FrameEditorController.OversetMessage`)

## 8. Documentation

`docs/accessibility-test-script.md` §3 is rewritten. It still described an **Object menu (`Alt+J`)**
with sixteen flat items and an **eighteen-control toolbar**, neither of which had existed since M11
— a manual test script that walks a menu bar the app does not have is worse than no script, because
a tester following it records "not found" against a dozen items and learns nothing. §3 is now ten
subsections covering all nine menus, the nine-control toolbar, and a new **§3.11** asking the tester
to read the plain-language *reason* on any greyed item, which M11 added and the script never
mentioned. Stale `Object ▸` paths in §4, §7, §9 and §11 follow the new homes.

## 9. Scope

Everything in the plan's deliverable list is in. Nothing from the "scope cut order" was cut: the
hover outline, the status wording, and the cursor caching are all present.

**Not done, and deliberately:** the `OnLostFocus` text-session investigation. The plan listed it
third in the cut order and said "fix only if cheap, else it moves to M21". It is not cheap —
`OnLostFocus` ending the session is the M4 v1 decision (docs/M4-spec.md §7.5), and changing it means
deciding what happens to a caret while a dialog is open, which is a behaviour question, not an
affordance one. **It moves to M21.**

## 10. Open

- **A manual NVDA pass over the new menu bar** (PLAN.md §11 M17 acceptance). Needs a person; it is
  tracked with the other blocked manual items in PLAN.md §13.
- **The hover outline has no automated test.** It is drawn from `_hoverBlockId`, which only a real
  pointer move sets; the headless session can set the field but the assertion would then be that the
  field it just set has the value it set. `HoverBlockIdForTest` exists for a future pointer-driven
  test.
