# M33 — The three PLAUSIBLEs

**Delivered 2026-08-07.** The review's three remaining "argued from the code, never observed"
findings, each taken as far as evidence allowed before anything was changed. Two reproduced and were
fixed. One did not, and the reason is recorded rather than the finding being quietly dropped.

---

## 1. AutoLevels on an `Unpremul` target — NOT reproduced

The review argued that `Apply` draws through an `SKCanvas` wrapped over a bitmap allocated
`SKAlphaType.Unpremul`, that Skia raster devices want premul or opaque, and that on some builds the
draw would therefore be a silent no-op producing a transparent or black result.

`ImagingAlgorithmTests` already contains three tests that call `Apply` and **read the resulting
pixels back** — `AutoLevelsStretchesAWashedOutPhotoToTheFullRange` asserts the stretched luma range,
and `LuminanceModeKeepsAWarmPhotoWarm…` asserts channel ordering in the output. A no-op draw fails
all of them loudly. They pass on Windows, Linux and macOS in CI.

So the failure is not reachable on any platform this app ships to, and the existing tests are
already the guard. **Nothing was changed.** Recorded here because "we looked, and here is why it is
fine" is worth more to the next reader than silence.

## 2. The histogram read premultiplied pixels — reproduced and fixed

The related Minor finding turned out to be the real one.

`ImageDecoder` decodes to `Premul`, so a half-transparent pixel sits in memory with its colour
already scaled by its alpha: a mid grey at 50% alpha is stored as a dark grey. `Histograms` skipped
fully transparent pixels but counted partly transparent ones **raw**, so they dragged the measured
black point down and the picture was then stretched against a level no pixel actually had.

Measured: a ramp with a floor of 80, stored at 50% alpha, reported a black point of **40**.

Channels are now un-premultiplied before counting, rounded and clamped because premultiplication is
lossy. Fully opaque pixels — nearly every pixel of nearly every photograph — skip the divide
entirely.

## 3. The speculative overset check ignored captions — confirmed and fixed

`DocumentRenderSource.WouldStillBeOverset` asks "would one more frame be enough?" by building a
speculative layout. It called `BuildSpeculativeRequest`, which passed `rectOverrides: null` — while
the **real** layout at `EnsureLayout` passes `_layoutRects`, which hold the caption-extended rect of
every captioned photo.

So the speculative layout wrapped text around a smaller obstacle than the real page did, fitted more
text than reality would, and could answer "yes" for an article that is still overset once the frame
is committed. The auto-flow decision was being made against a different page from the one the user
is looking at.

`BuildSpeculativeRequest` now takes the overrides, and applies them to the planned frames as well as
the real ones.

## 4. Composite rollback — confirmed and fixed

`CompositeCommand.Apply` ran its children in order with no unwind. A composite is the app's unit of
"one undoable thing" — adding a text frame is a story *and* a block; filling in the officers is a
dozen row rewrites. If a child threw part way through, the earlier ones had already mutated the
document, and `DocumentSession.Execute` never reached the line that pushes the command onto the undo
stack.

The result was a half-applied change **the user could not take back**, because as far as the stack
was concerned it had never happened.

`Apply` now unwinds what it managed to do before rethrowing. The unwind is best-effort — a `Revert`
that throws while cleaning up after another throw has nothing useful to say, and letting it escape
would replace the real exception with a confusing one — but the original exception always reaches
the caller, which is what tells the shell to say something.

---

## 5. What guards it

- `ImagingAlgorithmTests.PartlyTransparentPixelsAreMeasuredByTheirRealColourNotTheirPremultipliedOne`
  — two bitmaps holding the same colours, one premultiplied; the levels must agree. Confirmed to
  report 40 instead of 80 without the fix.
- `CommandTests.ACompositeThatFailsPartWayThroughUndoesWhatItAlreadyDid` — the document snapshot is
  unchanged and the undo stack is empty. Confirmed to fail without the fix.
- The speculative-layout change is a threaded parameter; the existing auto-flow tests cover the
  path and stayed green. A test that could tell the two layouts apart would need a captioned photo
  positioned to change the answer, which is a fixture worth building when auto-flow is next touched.

Suite after M33: **1174 passing, 12 skipped**. No snapshot baseline moved.
