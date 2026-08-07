# M31 — Four more from the list

**Delivered 2026-08-07.** Four defects from §14.2, all of which were labelled PLAUSIBLE or Minor and
all of which turned out to be real on reading the code.

---

## 1. Tabs were deleted from pasted text

`TextEditorController.Sanitize` filters out control characters, and `char.IsControl('\t')` is true —
so a tab was removed rather than replaced. Columnar text pasted from a spreadsheet or an email
arrived with its words run together: `"Name\tOffice"` became `"NameOffice"`.

The layout engine has no tab stops, so a tab genuinely cannot be honoured. It now becomes a space —
the separator it was standing in for — which at least keeps the words apart.

## 2. Auto-flow put continuation frames underneath the page

`PageFlowController` created continuation frames with `ZOrder = 0`, regardless of what was already
on the target page. Every other insert path in the app — `FrameEditorController`, `PhotoController`,
`WidgetController` — uses `Max(ZOrder) + 1`.

So an article flowed onto a page that already had a picture on it could land **beneath** that
picture and be wrap-shadowed by it: text disappearing into a frame the user cannot see it in.

## 3. Two getters handed out their own mutable state

`GetOversetTailBlockIds` and `GetWidgetOverflowBlockIds` returned the live internal `List<string>`,
which the next relayout `Clear()`s in place. A caller that held the result watched it silently
empty itself, with no hint that what it was reading had been recycled underneath it. Both return a
copy now.

## 4. Three page-indexed methods threw the wrong exception

`GetPageSize`, `RenderPageToPng` and `DescribeBlocks` indexed `_document.Pages` raw, so an
out-of-range page produced a bare `IndexOutOfRangeException` instead of the guarded
`ArgumentOutOfRangeException` their siblings produce.

This matters more than tidiness: M25's compositor-thread guard in `PageDrawOperation.Render` catches
`ArgumentOutOfRangeException` by name, and the stale page index it exists to survive arrives through
`GetPageSize`. The guard was catching the exception this method did not throw.

---

## 5. What guards it

- `TextEditorControllerTests.PastedTabsBecomeSpacesRatherThanVanishing` — run against the unfixed
  code and confirmed to fail.
- The other three are structural: a z-order expression matching three sibling call sites, two
  defensive copies, and three argument guards. The existing suites cover the paths and stayed green.

Suite after M31: **1171 passing, 12 skipped**. No snapshot baseline moved.
