# M101 — The menu taxonomy pass

**Delivered 2026-09-05.** The menu bar reorganised so that the ten milestones before it stop being
the reason the next command cannot be added.

## Why

M91–M100 added fifteen commands and hit the same wall three times: **Format, Insert and Arrange had
run out of access keys.** Three milestones ended up putting their commands in an ad-hoc submenu, and
two commands had to be renamed after a collision — `Ctrl+E` has meant "Make the PDF" since M8, and
the bare word "colours" has meant the app's own appearance since M16.

`NoTwoItemsInOneMenuShareAnAccessKey` and `EveryAdvertisedGestureIsUniqueAmongTheMenus` caught every
one, which is the M11 surface working exactly as designed. But a test that keeps saying *no* is
telling you something about the structure, not about the command being added.

Before this pass: **Arrange had 3 free letters, Insert 6, Format 6.** Two more commands and the
menus would have been unable to accept a third.

## What shipped

Still **nine top-level menus** — the bar wraps onto a second row at 200% scale with a tenth, which
costs the page a strip of height on every window, and that is a worse trade than any nesting. What
changed is what is inside them.

| Menu | Direct items before → after | Free access keys before → after |
|---|---|---|
| File | 21 → 14 | — → 12 |
| Format | 20 → 6 | 6 → **20** |
| Insert | 20 → 7 | 6 → **19** |
| Arrange | 24 → 9 | **3** → **17** |
| Page | 12 → 10 | — → 16 |

Every menu now has at least twelve free letters and at most fourteen direct rows.

## The seven new submenus

Each one is a group that answers a single question, named in words a committee member would use:

- **File ▸ Check it over** — look it over, spelling, last year's, read it back. Four ways of
  checking one newsletter, which sat as four rows between Save and Make the PDF.
- **File ▸ My templates**, **File ▸ Handing over** — two pairs, each a subject of its own.
- **Format ▸ How the writing looks** — colour, bigger, smaller, spacing, capitals, the "just here"
  font and putting it back, fonts and styles.
- **Format ▸ Which way it lines up** — M96's three, regrouped rather than left ad hoc.
- **Insert ▸ A lodge list** — the eight built-in lists and the two commands that fill two of them
  from the address book. Ten rows that were half the Insert menu.
- **Insert ▸ Something from elsewhere** — writing from a file, a page from a PDF, an emblem.
- **Arrange ▸ How the writing flows**, **▸ Front to back**, **▸ Line them up** — three subjects that
  were eighteen flat rows.

**What deliberately stayed at the top level**: Bold and Italic, the two list verbs, Make the PDF,
Print and Send. They are pressed far more often than everything nested under them put together, and
burying the export behind a submenu would be the worst trade in the whole pass.

## Two items were in the wrong menu, and this is where that got fixed

- **"Change what colour the box is…" was in Format.** It is a property of an object on the page, not
  of writing. It landed there in M95 only because Arrange had no access key left — the constraint
  choosing the taxonomy, which is backwards. It is now in Arrange with the other object properties.
- **"Move what is chosen to the next page" was in Page.** Same story from M91: it acts on the
  selection, not on the page, and it went to Page because Arrange was full. Now in Arrange.

That both misplacements were caused by the same shortage is the argument for doing this pass at all.

## The defect the generator found

The menu was rebuilt by a script that moves each existing `<MenuItem/>` **verbatim** — every `Tag`,
`InputGesture` and `AutomationProperties.Name` preserved — and recomputes access keys per scope. The
first run failed `NoTwoItemsInOneMenuShareAnAccessKey` with a collision that did not exist in the
XAML.

**Four menu items have their `Header` rewritten at run time** by `RefreshActions`, with an access
key chosen in C# rather than in the markup: Undo and Redo grow a description, Save grows an ellipsis
when there is no file yet (M24), and the two picture commands change wording with what is in the
frame (M18). The catalog owns those words, so the menu cannot — which means the generator was
handing those letters to a neighbour, and the two only collided once the app was running.

The generator now reserves them, and `picture.replace` reserves **both** P and S, because its title
switches between "Put a picture here…" and "Swap this picture…" and either letter may be the live
one. It also keeps each item's **original** access key wherever that letter is still free, so a
mnemonic somebody has learned survives the reshuffle.

## What did not need changing

`MenuPaths` reads the menu bar rather than hard-coding paths (M63's decision), so every help topic's
"where to find it" line re-derived itself through a whole-bar restructure with no edit and no stale
claim. That is the payoff for a decision made two dozen milestones earlier.

## Verification

`dotnet test TrestleBoard.slnx` — all green, no snapshot baseline moved (nothing here reaches the
document or the renderer). All 142 menu items are still present and still carry their tags:
`EveryActionInTheCatalogHasAMenuItem` passes with `MayLiveOutsideTheMenus` still empty,
`EveryMenuGestureIsTheOneTheKeyboardTableDispatches` passes, and `TheMenuBarStaysNineWide` passes
with the same nine headers it has pinned since M17.
