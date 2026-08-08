# M41 — Save on the toolbar, and the state beside it

**Delivered 2026-08-08.** Closes the last part of §14.4's save-surface finding, which M24 only
partly addressed.

---

## 1. What was left

M24 gave the app a Save command, a Ctrl+S, a menu item and a title bar that says "not saved yet" in
words. It closed §14.1. What it did not do is put any of that on **the toolbar** — and the review
said, in as many words, that the toolbar is where this audience looks first.

So the most-used command in the program was reachable from a menu or a chord, and the answer to "is
my work safe?" lived in a title bar, which is not somewhere anybody looks to ask it.

## 2. What changed

**A Save button**, second from the left, next to Open. Its label is "Save", which is a prefix of the
catalog's "Save this newsletter" — that is what keeps `PlainLanguageTests`' toolbar-versus-catalog
rule satisfied, and it is the same word the menu uses.

**A save-state label beside it**, a polite live region, saying exactly one of three things:

| State | Text |
| --- | --- |
| No newsletter open | *(blank)* |
| Saved | `Saved` |
| Edited since | `Not saved yet` |

Blank rather than "Saved" when nothing is open: "Saved" over an empty window answers a question
nobody asked.

## 3. What was NOT done, and why

**No New button.** The finding named "Save/New", and Save is in. New is not, deliberately: the
toolbar label must match the catalog per M27, the catalog calls it "Start from a template…", and a
button that wide on an already-scrolling toolbar costs more than it gives. This committee makes one
newsletter a month — New is a once-a-month act that the start screen already offers as a large
card, while Save is a once-a-minute one. Adding a rare wide button beside a constant one makes the
constant one harder to find.

## 4. Screenshots

**Every main-window shot re-baked, and more moved than this milestone touched.** M37's action-button
borders and M40's destructive button had never been re-baked into the dialog shots — `settings.png`,
`grid-editor.png`, `import-columns.png`, `wizard-*.png`, `restore-dialog.png`, `officers-sync.png`
and `font-picker.png` were all stale against code that had already shipped. Seventeen images moved;
only some of that is M41's doing, and the rest is a backlog being paid off rather than a change.

All regenerated from fictional fixtures against a temporary app-state root, with no text chunks.

## 5. What guards it

`SaveShellTests.TheToolbarCarriesSaveAndSaysWhetherTheWorkIsSafe` — the button is on the toolbar
carrying `newsletter.save`, and the label reads blank, then "Saved", then "Not saved yet", then
"Saved" again across an open, an edit and a save.

Suite after M41: **1196 passing, 12 skipped**. No snapshot baseline moved.
