# M43 — The overset marker says what it means, and "I know" survives closing the app

**Delivered 2026-08-08.** Two §14 findings that are both about the app telling somebody something
they cannot act on.

---

## 1. A red square is not a sentence

The overset marker has been a 12-pixel red square with a white "+" since M5 — the InDesign
convention, which is the right convention for people who have used InDesign. This committee has not.

It was never the *only* signal: the shell has shown a plain-language status line since M17. But the
status bar says it only while nothing else is talking, and the review is right that a twelve-pixel
glyph at the edge of a frame, on a page that already has ink all over it, is not something this
audience will notice.

**The badge now carries the words "does not fit" beside it**, on a plate so they stay readable over
whatever the page is showing, sized in screen points so zooming out to see the whole page does not
shrink the one thing saying the page is wrong.

**The badge itself is unchanged, deliberately.** Growing it was the first thing I tried, and it moved
`frame-selection-handles` — a baseline that would then need re-baking on three operating systems for
six pixels of red square. The words are the fix; the square never was.

The renderer takes an optional typeface, because the PDF export and the snapshot suite draw overlays
with no font in hand, and a renderer that *demanded* a font to draw editor chrome would be asking
every caller for something only one of them has. The shell hands over its bundled body face rather
than the canvas loading a second copy of the same files.

## 2. "I have already seen this" died with the session

M23's stretched-picture note is dismissible. The dismissal lived in a `Dictionary<string, float>` on
`PhotoController`, so it lasted until the app closed — and on the next open the note came back, with
the app arguing with somebody who had already answered.

It now lives in `ImageRecipe.StretchNoticeDismissedAtAspect`, which the document carries and the
file keeps:

- **Still keyed by aspect**, so M23's rule is intact: reshape the frame and the note returns,
  because that is a *new* mismatch rather than the one that was dismissed.
- **Nullable and additive**, so documents written before M43 deserialize unchanged and need no
  migration.
- **An ordinary recorded change**, so it goes on the undo stack. The cost is honest and small:
  dismissing the note marks the newsletter as edited, because it now genuinely is.

## 3. What guards it

- `OversetLabelTests.TheOversetMarkerSaysDoesNotFitInWords` — **deliberately not a snapshot test.**
  A baseline would have to be baked on three operating systems for a marker whose whole point is
  that it is words. It counts ink in the region left of the badge instead: zero without a face, ~300
  pixels with one, on any machine. It also asserts the badge region is byte-for-byte unaffected,
  which is what keeps every existing baseline where it is.
- `OversetLabelTests.WithoutAFaceTheBadgeIsStillDrawn` — the fontless callers get the badge, not an
  exception.
- `PhotoControllerTests.DismissingTheNoticeIsRememberedInTheDocumentRatherThanTheSession` — the
  recipe holds the aspect, and undo takes it back.

**Honest note on verification.** Both new tests reference API that did not exist before this
milestone, so reverting the source makes them fail to *compile* rather than fail an assertion. That
is weaker evidence than M39's and M40's tests, which failed on a specific line for a specific
reason. The behavioural claims that could be checked the strong way — the badge region being
untouched, and undo restoring the note — are asserted explicitly.

## 4. What was NOT done

The status-bar sentence is unchanged. It says what to *do* about overset text and names the command;
the badge label says only that there is a problem, because it is drawn beside a frame edge and has
to fit. Three words there and a sentence in the status bar is the right division.

Suite after M43: **1201 passing, 12 skipped**. No snapshot baseline moved, no screenshot re-baked.
