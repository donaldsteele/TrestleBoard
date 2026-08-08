# M46 — The app stops speaking typography

**Delivered 2026-08-08.** Closes the jargon inventory in §14.3 — the last item in grouping 4.

---

## 1. The words

Every one of these was in a title the app showed to a lodge secretary. Each asks the reader to
already know the trade the app exists to spare them.

| Was | Is |
| --- | --- |
| Add a text frame | **Add a box for writing** |
| Wrap text around this | **Make the writing flow around it** |
| Fit to contents | **Make it fit what is in it** |
| Bring forward / Send backward | **Move it forward / Move it back** |
| Bring to front / Send to back | **Move it to the front / Move it to the back** |
| Export as PDF… | **Make the PDF…** |
| "This item" (panel group) | **The thing you chose** |
| "A text frame is selected" | **A box of writing is chosen** |
| "You are typing" | **You are writing** |
| "…still showing placeholders" | **…still showing empty picture frames** |

Two decisions worth stating:

- **"PDF" stays.** It is the thing they email, it is printed on every button in every other program
  they use, and there is no plain synonym. "Export" is the jargon in that phrase, not "PDF".
- **"Selected" became "chosen" throughout the panel heading.** Selection is a computer word for a
  computer idea; a person chooses a thing.

## 2. A test, not a commit

The renaming is the small part. The finding is a *class* of defect, so it is now a standing rule:
`ActionCatalogTests.NoCommandSpeaksToTheUserInTradeVocabulary` walks every catalog entry and fails
on seventeen banned words — the ones above plus `overset`, `z-order`, `kerning`, `leading`,
`gutter`, `bleed`, `recto`, `verso`, which are not in the app today and should not arrive.

Verified failing against the old catalog, naming all seven offending commands.

The exceptions are named and small: "PDF", and "Undo"/"Redo", which are the two words every program
on the machine agrees on.

## 3. Access keys

Every renamed menu item needed a new mnemonic, and three of them collided on their first choice.
`AccessibilityTests.NoTwoItemsInOneMenuShareAnAccessKey` is what settles this — the same test that
caught M38's `S` clash.

## 4. What was NOT done

**`SelectionKind.Shape` still says "A shape is selected".** The review notes that nothing in this app
creates a shape, and it is right: the kind is reachable only as the fall-through for a block that is
none of the three real kinds. Renaming a string nobody can see would be tidying, not fixing; it is
left as-is and recorded here.

**The wizard's two commit verbs** ("Show all at once" versus "Save it") are untouched. They are part
of the wizard finding in §14.4, which needs the grid-versus-wizard confirmation inconsistency
settled with it.

Eight screenshots re-baked. Suite after M46: **1205 passing, 12 skipped**.
