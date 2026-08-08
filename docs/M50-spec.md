# M50 — The last two keyboard findings, and a defect in the tests themselves

**Delivered 2026-08-08.** Closes the two §14.3 items M49 left open by choice, and fixes a fault in
the headless suite that this milestone's own verification exposed.

---

## 1. Add-to-selection: the review named a mechanism, and it was the wrong one

Shift+click adds one thing at a time to what is already chosen, and had no keyboard equivalent. The
review's framing was that the missing piece is *"a cursor that moves independently of the
selection"* — a second highlight driven with the arrow keys and committed with Space, the way a
Windows list box works.

That mechanism is real, and it is the wrong one here. It introduces a **mode** this audience would
have to be taught, to reach an outcome they can state in one sentence: *and that one too*.

So **Ctrl+Tab keeps everything already chosen and adds the next thing** on the page, walking the same
stacking order `CycleSelection` walks — so Tab and Ctrl+Tab agree about what "next" means. Press it
three times and four adjacent things are chosen, ready for the align commands. Ctrl+Shift+Tab walks
the other way. No cursor, no mode; the relationship of Ctrl+Tab to Tab is exactly Shift+click's
relationship to a click.

Two details that are load-bearing:

- **It walks outward from the last thing added**, not from the primary — otherwise repeated presses
  flip between the first frame's two neighbours instead of travelling.
- **It skips what is already chosen**, so a press either adds something or says *"Everything on this
  page is already chosen"*. It never appears to do nothing.

With nothing chosen it simply chooses one, which is the same generosity `AddToSelection` has always
shown a first Shift+click.

## 2. Pointer-anchored zoom: no pointer, but there is still an anchor

The review asked for pointer-anchored zoom from the keyboard and named the obstacle — without a
pointer there is no anchor. True, and not the end of the question: **when something is chosen, the
application already knows what the user is looking at.**

`StepZoom` — the toolbar buttons, Ctrl+= and Ctrl+− — now anchors on the chosen thing's centre, and
falls back to zooming about the middle of the view when nothing is chosen, which is what it always
did. It routes through M21's `ZoomAtPointer` rather than repeating its arithmetic: the anchor is a
point on the page either way, and that method already knows how to hold one still across a zoom step.

`ZoomAtPointer` itself now calls a new unanchored `StepZoomAboutTheCentre`, or it would recurse back
through the selection anchor it is trying to override.

## 3. The defect this milestone found in its own tests

Verifying the fix meant running the suite against deliberately broken code, and the results were
nonsense: **every test in the assembly reported `KeyNotFoundException: 'fonts:SystemFonts'`** — the
dead-session symptom from [[m39]]'s finding — rather than its own assertion.

The cause is in the tests, and it is mine, repeated across the whole campaign:

```csharp
// … twenty lines of assertions …
window.SaveFirstAnswerForTest = MainWindow.SaveFirst.Discard;   // ← set LAST
window.Close();
```

If any assertion above it throws, **the flag is never set**. `Close()` then raises the
unsaved-changes dialog, which a headless run cannot answer, and the shared session hangs — so every
*other* test reports the dead session instead of its result. A single genuine failure was
disguising itself as twenty environmental ones.

All **21 occurrences** are moved to immediately after `window.Show()`. A failing assertion now fails
loudly and alone, which is what `DispatchDisciplineTests` and the M39 rule were for in the first
place. This is the same class of defect as M39's: **the suite reporting something other than what it
was asked**.

## 4. What guards it

- `FrameShellTests.ShiftClickHasAKeyboardEquivalentThatKeepsWhatIsAlreadyChosen` — three presses,
  three things, the first still chosen, and the align commands available afterwards.
- `FrameShellTests.AlsoChooseTravelsAndThenSaysThereIsNothingLeft` — presses travel rather than flip,
  and one press too many is refused in words.
- `FrameShellTests.ControlTabReachesTheAlsoChooseCommands` — **worth its own test**: Tab is a key the
  framework has opinions about, and if Avalonia's focus navigation swallowed Ctrl+Tab the menu would
  advertise a gesture that does nothing. It does not; the command is reached.
- `FrameShellTests.ZoomingFromTheKeyboardKeepsTheChosenThingInView` — zooming with a frame near the
  bottom of the page scrolls towards it, which zooming about the centre would not do.

### Honest note on the evidence

**This milestone's failure-first verification is weaker than M40's, and the reason is worth
recording.** Running a *filtered* subset of the headless assembly reliably kills the shared session
in this environment, so filtered runs cannot be used as evidence at all; and the full assembly, run
against the deliberately broken version, hung rather than reporting. What I have is: all four tests
pass against the fix, Ctrl+Tab is proven to reach the command, and the broken build demonstrably
changes behaviour. What I do not have is a clean assertion failure on a named line, which is the
standard M39 set. Stated rather than implied.

## 5. What was NOT done

**No marquee.** A rubber band is a pointer gesture; M49 already established that the keyboard should
get the marquee's *purpose*, and between "Choose everything on this page" and "Also choose the next
one" it now has both the whole-page and the some-of-them cases.

Suite after M50: **1217 passing, 12 skipped**. No baseline moved, no screenshot re-baked.
