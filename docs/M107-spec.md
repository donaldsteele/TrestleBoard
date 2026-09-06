# M107 — Leaving the front page out of the numbering

**Delivered 2026-09-06.** The register's "no page-numbering options" row, answered with the one
option anybody has ever wanted.

## Why this and not a numbering dialog

The register listed the absence as: no format, no start-at, no restart, no suppress-on-first-page,
nothing about numbering stored at all. Four of those five are questions nobody on this committee has
asked. The fifth is the one every printed newsletter already answers by convention: **the cover is a
cover.**

"Indian Land Lodge 414 · September 2026 · page 1 of 6" printed under a cover heading that already
says the lodge and the month says all three facts twice, in the place the eye reads first. So this is
a toggle, not a window of options — a numbering dialog would be four questions asked in order to
answer one.

## The three decisions

**Phrased as `HideFooterOnFirstPage`, defaulting to false.** The additive rule `ShowFooter` follows:
a newsletter written before this has no such property in its file and must open looking exactly as it
did yesterday. With the positive phrasing — `FooterOnFirstPage`, defaulting to true — a missing
property would have meant "stop printing the number on page 1" for every newsletter already written.

**It says nothing about the count.** Page 2 is still "page 2 of 6". The cover is still a sheet in the
reader's hand, and a footer that disagreed with what they can count would be worse than no footer at
all. Renumbering so the cover is page 0 was rejected on that basis.

**Every master at once**, like `ShowPageFooterCommand` beside it. The footer is one decision about
the whole newsletter; a per-master answer would be a setting whose effect depends on which master a
page happens to use, which is not something this audience can see or reason about.

## The refusal

With the line turned off altogether the question is meaningless, so the command is blocked — and the
refusal offers `page.footer`, the toggle that turns it on, rather than leaving somebody to work out
which of the two they actually wanted.

`ActionContext` gains `PageFooterShowing`, read off the first master rather than kept as a second
copy: two things that can disagree eventually will, and here the disagreement prints the wrong page
(the M55 rule).

## Verification

`dotnet test TrestleBoard.slnx` — green, **no snapshot baseline moved**: nothing renders differently
until somebody sets the flag, which is what "additive" has to mean in a renderer.

Seven new tests. The load-bearing one renders page 0 **and page 1** and asserts the cover changed
while the inside page did not — a rule that took the footer off every page would pass a test that
only looked at the cover, and would be the feature the committee already has under a different name.
**Mutation-checked**: dropping the `pageIndex == 0` clause fails exactly that test.
