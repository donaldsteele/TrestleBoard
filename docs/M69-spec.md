# M69 — The two the owner found

**Delivered 2026-08-09**, in `bc5b9ae` and `dcb7ef5`. PLAN.md §11 M69.

> **Written on 2026-08-10, and the delay is part of the record.** This document is one of three
> (M69, M70, M71) that the spec-doc convention held for M6–M68 and then skipped — on precisely the
> three milestones whose value was their exclusion list. M73 (i) is the correction. Everything below
> is reconstructed from PLAN.md §11 M69, the two commits and their tests; where the original
> reasoning cannot be recovered, §5 says so instead of guessing.

## 1. What this milestone was

Not a plan item. Two defects the owner reported from ordinary use on the afternoon M63–M67 shipped,
each with a screenshot. The heading in PLAN.md was written afterwards, once it was clear the two
had a shape in common — the code in seven files cited "M69" before §11 had an M69 in it.

## 2. Dragging a picture's corner cropped it

**What was examined.** The frame-resize path from the canvas handle down to
`FrameGeometry`, and the meaning of every `ImageFit` value in the model.

**What was found — two independent faults, both needed for the bug.** A corner drag moved both
edges independently, so the frame's aspect changed. And reshaping a picture *frame* does not reshape
the *picture*: `ImageFit.Cover` answers a changed aspect by cropping the source to it. "Make this
bigger" quietly became "crop this", and M22's "Choose which part shows" could only pan the crop
window, never widen it — hence the owner's "no easy way back".

`ImageFit.Contain` had been in the model since M2 and **nothing had ever set it**. The value was
written and never called, which is why the bug had no workaround at all.

**The fix.** `FrameGeometry.ResizeKeepingAspect` scales from a corner, anchoring the opposite corner
so the frame grows away from the hand, and following whichever edge the pointer pulled further so a
diagonal drag tracks the hand rather than snapping to whichever axis the code reads first.
`FrameEditorController` routes a drag through it only when the block is an `ImageFrame` and the
handle is a corner. Emblems and PDF pages now arrive as `ImageFit.Contain`.

**What was deliberately excluded, and why.**

- **The side handles still reshape.** That IS how somebody chooses a different crop on a
  photograph, and taking it away would remove the only direct way to do it. A corner scales; an edge
  reshapes.
- **A photograph keeps `ImageFit.Cover`.** Filling the frame is what a photograph is for, and M22
  exists to choose what shows. Only an emblem and a PDF page changed, because a cropped square and
  compasses is a mutilated symbol, not a cropped photograph, and a cropped PDF page loses content.
- **A text frame still reshapes from its corner.** Reflowing the words is the point there.

**Tests.** `DraggingAPictureCornerKeepsItsShape` fails without the geometry change and passes with
it. Four more hold the parts that must *not* change — an emblem's fit, a photograph still on
`Cover`, a side handle still reshaping, and a text frame still reshaping from its corner.

## 3. Buttons and chrome borders overlapped or escaped their containers

**What was examined.** Every scrolling container in the shell chrome.

**What was found — two symptoms, one cause.** Avalonia auto-hides a scrollbar and draws it *over*
the content. Every action-panel button is stretched to the full panel width, so the vertical bar sat
on top of their right-hand edges and read as a grey line running down through the offers. And
because an auto-hiding bar appears only once the pointer is already moving over it, nothing on
screen said the list continued below the fold.

The toolbar had the same fault in the other axis. It has scrolled horizontally since M9 *precisely*
so that a window narrower than the toolbar cannot put a command out of reach — the comment above it
says "a control the user cannot reach is the same as a control that is not there". But the bar
auto-hid, so the last button was simply cut off at the window edge with nothing to say it could be
scrolled to. **The guard was there and invisible.**

**The fix.** `AllowAutoHide=false` on both. The bar takes a layout column of its own, so the panel's
buttons end short of it and the toolbar's overflow announces itself.

**Where it mattered most.** 200% UI scale, which is where PLAN.md §6 cares most; `scale-200.png`
now shows a large scroll track with arrows at both ends.

**What was deliberately excluded.** Eight screenshots were regenerated — every one that frames the
toolbar or the panel, and **no others**. `BadBorder.png` and `ResizeBug.png` were deleted from the
repository root: they were the reports, not artifacts, and `docs/images` is generated.

## 4. The same shape, twice more the same day

Two further reports landed while these were being fixed, and they are the reason M70 exists rather
than a third bug fix:

- **The paragraph-style flyout opened and shut again** (`d3b797b`). It was anchored to a control the
  action-panel rebuild had already destroyed.
- **The spelling walk refused into a status bar behind the window being read** (`8a73929`). Two
  defects there, not one: a stale paragraph offset after a correction of a different length, which
  the guard correctly refused; and the refusal being announced into the main window's status bar,
  behind the 620×700 window the user was looking at, so the report read "no error, nothing happens".

## 5. What this milestone did NOT examine — stated rather than implied

**M69 ran no audit.** It was two bug reports and their fixes. There is therefore no exclusion list
of the kind M70 and M71 produced, and none is invented here. The only deliberate omissions are the
three in §2 (side handles, photographs, text frames) and the screenshot scope in §3.

**What the two had in common, which was the finding.** Neither was a broken mechanism.
`ImageFit.Cover` did exactly what it says; the scrollbar drew exactly where Avalonia draws it. In
both, the app did the right thing and the user could not see what it had done. That observation —
plus the two later the same day — is what prompted the three whole-app audits of M70.

## 6. What was not done

- **No change to the model.** `ImageFit.Contain` already existed; this milestone was the first
  caller.
- **No new availability rule, no new `ActionId`.** Both fixes are behavioural, inside existing
  commands.
- **No snapshot baseline moved.** The regenerated screenshots are documentation
  (`tools/TrestleBoard.Screenshots`), not layout baselines.
