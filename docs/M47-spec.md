# M47 — The edge to keep inside

**Delivered 2026-08-08.** Closes the "no rulers, guides, margin display, or grid" finding in §14.3 —
by drawing the one of those four that this audience can use, and declining the other three on the
record.

---

## 1. The app knew and would not say

`PageMaster` has carried four margins since M2:

```csharp
public float MarginLeftPt { get; set; } = 54f;
public float MarginTopPt { get; set; } = 54f;
public float MarginRightPt { get; set; } = 54f;
public float MarginBottomPt { get; set; } = 54f;
```

Nothing has ever drawn them. So "is this frame too close to the edge?" — a question a newsletter
committee asks every month, right before the printer answers it for them — was one the app held the
answer to and would not show.

**View ▸ "Show the ed_ge to keep inside"** draws it as a faint dashed rectangle. Off by default, for
the same reason the font-change marks are: a line nobody asked for is noise on a page they are
trying to read.

It is an **editor adornment**, drawn with Avalonia's own primitives like every other adornment on
this canvas. That is what keeps it out of the PDF (the exporter draws through `RenderPage`, which
knows nothing about it) and out of every snapshot baseline.

## 2. Rulers, grids and draggable guides: declined, with reasons

The finding groups four things. Three are not being built, and that is a decision rather than a
deferral:

- **Rulers.** A ruler exists so you can position something by measurement. Nothing in this app asks
  anybody to: frames are dragged, and snapping already lines them up with each other and with the
  margins. A strip of inch marks along two edges of the window would cost screen space on a 4–6 page
  newsletter and answer a question this workflow never asks.
- **Draggable guides.** They are a professional's tool for a layout repeated across dozens of pages.
  This is six pages a month from a template that already has its structure.
- **A grid.** Same argument, plus it competes with the snapping that is already there and better —
  snapping aligns things to *each other*, which is what actually looks wrong when it is off.

If this turns out to be the wrong call, the margin rectangle is the natural place to grow from.

## 3. The access key

"Show the edge to keep _inside" collided with "Zoom _in" on **I** — the third time
`AccessibilityTests.NoTwoItemsInOneMenuShareAnAccessKey` has caught one of these (M38, M46, now).
Moved to **G**.

## 4. What guards it

`ConveniencesShellTests.TheEdgeToKeepInsideCanBeShownAndIsOffToStartWith` — off to begin with, the
command is available with a newsletter open, toggling says so in the status bar including that it
never prints, and the rectangle it draws is the master's actual margins rather than a guess.

Suite after M47: **1206 passing, 12 skipped**. No baseline moved, no screenshot re-baked.
