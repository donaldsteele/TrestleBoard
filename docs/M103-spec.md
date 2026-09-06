# M103 — A new line without a new paragraph, and copying how writing looks

**Delivered 2026-09-05.** The last two of the register's "small everyday verbs".

## The soft line break

**Another one that was already built.** `LineBreakAnalyzer` has listed `U+2028 LINE SEPARATOR`
among its mandatory breaks since M1, `TextLayoutEngine` obeys mandatory breaks, and the editor's
sanitiser lets it through because it is not a control character. **Every part of it worked and no
key put one in.**

That makes it the fourteenth instance of the category the audit named, and the cheapest of the lot:
a command, a keyboard row and a menu item.

`text.lineBreak` — "Start a **n**ew line, same paragraph", Shift+Enter.

**What it is for**: an address, a list of names, a heading that runs to two lines. Pressing Enter
there starts a new paragraph, which takes the paragraph gap and the first-line indent with it — so
the committee has been living with headings that are two paragraphs pretending to be one.

### A wording bug it exposed

`EveryShortcutTheCatalogAdvertisesIsInTheTable` failed on "Shift+Enter", because Avalonia's
`Key.Enter` **is** `Key.Return` and the enum reports the older name — so the app would have printed
"Shift+Return" beside a key every keyboard in the lodge calls Enter. `Describe` now maps it, next to
the `OemPeriod` mapping that exists for the same reason.

## Copying how writing looks

`text.pickUpLook` ("Copy how this looks") and `text.putLookDown` ("Make it look the same") — the
format painter, named for the job rather than the tool, because "format painter" is a picture of an
implement this audience does not know it is holding.

**It carries a style NAME, not a bundle of attributes.** Everything about how a run looks is already
a named style applied by reference — the locked constraint (§1, M14) — so carrying the name carries
the font, the size, the colour, the bold, the italic and the underline **together**, and putting it
down is one `ApplyCharacterStyleCommand`. Copying attributes instead would mean re-deriving a style
at the far end and inventing a second way for two runs to look the same.

**The look is not forgotten after one use.** Somebody making six headings match does it six times,
and a painter that emptied itself would make them pick the look up between each — the behaviour
people complain about in other programs.

**Two separate refusals.** Picking a look up needs a caret; putting it down needs a highlight *and*
something picked up. Each says which is missing and offers the command that supplies it, rather than
one sentence covering both cases badly.

## The performance test, third and final version

`SixtyDragStepsStayInsideTheFrameBudget` failed again. Its reasoning has been right since it was
written — a wall-clock assertion on a contended machine measures the neighbours — and the
**detection** has now been wrong twice: first keying on `CI` (so the strict budget applied when
running the whole solution locally, which contends the machine exactly as a shared runner does), then
counting sibling test processes (closer, but still a guess: the runner may start them sequentially
and still be busy finishing a build).

It has stopped guessing. By default it catches **gross** regressions only; `TRESTLEBOARD_PERF=1` on
an idle machine asks for the real 16ms frame budget. The precise, environment-independent gate was
always `OnlyThePagesStoriesRelayoutDuringADrag`, which counts layout passes rather than milliseconds
— and following that reasoning to its conclusion is what should have happened the first time.

## Verification

`dotnet test TrestleBoard.slnx` — all green, no snapshot baseline moved. Nine new tests in
`tests/Editing.Tests/LineBreakAndPainterTests.cs`, including the assertion that the layout engine
already knew how to break at `U+2028`, which is what makes "unreachable rather than absent" a claim
rather than a story.

One of them had to be rebuilt: the painter test pre-filled a frame and split it, but clicking into a
frame puts the caret at the **start** of the words, so the split left paragraph 0 empty and the
heading in paragraph 1. It types its fixture now.
