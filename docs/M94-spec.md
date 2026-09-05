# M94 — Size and place it exactly

**Delivered 2026-09-05.** A frame can be put at a typed position and size, in inches.

## What was wrong

Geometry could be set two ways: drag it with the mouse, or press an arrow key a point at a time.
That is the fine-motor task PLAN.md §6 exists to avoid, and it left a real gap for exactly the
audience the app is built for — somebody with a tremor could not put a notice where last month's
issue had it, and "nudge it 34 times" is not an answer.

`PositionPhotoWindow` looks like it fills this hole and does not: it pans the crop **inside** a
picture, and has no bearing on where the frame sits.

## What shipped

`item.positionAndSize` — "Si**z**e and place it exactly…" in the Arrange menu — four boxes: how far
in from the left, how far down from the top, how wide, how tall.

- **Inches, not points.** The model is in points because that is what the layout engine and the PDF
  speak; the committee measures in inches because that is what a ruler says. The conversion happens
  in the dialog, at the edge, and nowhere else.
- **The page size is printed on the window**, so the numbers have something to be relative to.
- **A bad number is refused with a sentence** naming which box is wrong, and everything else the
  user typed is kept. Typing a word where a measurement goes is an ordinary slip.
- **A frame kept in place refuses**, with the same sentence the drag gives — M28's rule, two ways
  in and one behaviour. A second route that quietly ignored the lock would make the lock worthless.
- **Numbers that would leave the paper are clamped back on.** A typed number can do what a drag
  cannot, and a frame the user cannot see is a change that appears not to have happened.

## The one ordering decision

`SetSelectionGeometry` emits the resize **before** the move. A resize takes the top-left as its
anchor, so the other order puts the frame where it was asked for and then drags it back off that
spot. Both go in one `CompositeCommand`, because moving and resizing at once is one thing the user
did and must be one Ctrl+Z.

Typing the numbers it already has returns false and runs nothing, so the shell says so rather than
leaving an empty step on the undo stack (M70(c)).

## Verification

`dotnet test TrestleBoard.slnx` — all green, no snapshot baseline moved. Six new tests in
`tests/Editing.Tests/PositionAndSizeTests.cs` covering the exact placement, the single undo step,
the clamp, the no-op, the lock refusal and the empty selection.

`NoTwoItemsInOneMenuShareAnAccessKey` failed on the first wording ("Say e**x**actly where it goes",
where X was already "Make the bo**x** taller") — Arrange has only J, Q and Z free. Rather than
carry a header whose access key came from nowhere in the phrase, the command was renamed so the key
falls inside a word it actually uses, and the catalog title, the menu header, the window title and
the accessible name were all moved together.
