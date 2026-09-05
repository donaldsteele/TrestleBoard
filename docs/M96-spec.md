# M96 — Which way it lines up

**Delivered 2026-09-05.** Paragraphs can be lined up left, centred or right. This is the first of
M86's three deliverables; underline and colour remain.

## What was wrong

`TextAlignment`, `ParagraphStyleDef.Align` and the shift arithmetic in `TextLayoutEngine` have all
been live since M1. Two sample styles use them. And no command in the app could reach any of it — a
committee wanting a centred heading over a notice had to mint a style by hand, which they will not do.

Another of the register's "built, working, unreachable" thirteen.

## What shipped

`text.alignLeft` (Ctrl+L), `text.alignCentre` (Ctrl+Shift+C), `text.alignRight` (Ctrl+R), in a
**Which wa*y* it lines up** submenu in Format.

**No justify.** §1 rules it out for v1, and rivers in a two-column frame are what it would give this
audience. M86 reaffirmed that, and this milestone does not reopen it.

## How it is carried

**A derived paragraph style, minted once per role and applied by reference** — exactly as bold and
italic ride on derived character styles. `body` centred becomes `body~centred`, reusing M14's `~`
convention. Nothing carries direct formatting, so the resolver, the serialiser and the canonicaliser
learn nothing new; a derived paragraph style is an ordinary paragraph style.

Three properties fall out of that and each has a test:

- **Centring one paragraph leaves the role alone**, so it does not centre every other paragraph in
  the newsletter sharing that role.
- **The derived style carries everything else across** — character style, line spacing, space before
  and after, first-line indent. Centring a heading must not quietly change how much air is round it.
- **Left is the absence of an override, not an override to left.** Every template style is already
  left-aligned, so a paragraph put back names its role again. A document that centres nothing looks
  byte-identical to one written before this existed, which is what keeps the canonical form small.

`EnsureParagraphStyleCommand` is the paragraph-level twin of `EnsureCharacterStyleCommand`, and
remembers whether *it* added the style, so undoing one centring cannot take a style away from
another paragraph that has since started using it.

## The two clashes, and what they cost

**Ctrl+E is not available for centring.** Every other editor uses it, and this app has spent it on
"Make the PDF…" since M8. An export is the more important command and it was there first, so centre
takes **Ctrl+Shift+C** — C for the word. `EveryAdvertisedGestureIsUniqueAmongTheMenus` is what
refused the alternative.

**Format had one access key left.** "Line it up on the **r**ight" collided with "Make this a
numbe**r**ed list", and "middl**e**" with "Make text small**er**". The three moved into a
**submenu**, which gets its own key namespace — the Picture submenu is the precedent — and they are
three items that belong together anyway.

Also renamed on the way in: `ActionId.AlignLeft`/`AlignRight` already existed for lining up *frames*.
The text ones are `AlignTextLeft`/`AlignTextCentre`/`AlignTextRight`, and the compiler caught it.

## Verification

`dotnet test TrestleBoard.slnx` — all green, **no snapshot baseline moved**: alignment is honoured
by the existing layout path and no fixture opts into it. Eleven new tests in
`tests/Editing.Tests/AlignmentTests.cs`, plus `EnsureParagraphStyle` identity coverage in
`CommandTests`.
