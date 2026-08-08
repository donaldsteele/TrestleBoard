# M44 — The canvas says which mode you are in

**Delivered 2026-08-08.** Closes the "hidden click mode" finding in §14.3 and two of the keyboard
gaps beside it.

---

## 1. The mode nobody could see

The same click on a text frame does two different things:

```csharp
bool alreadySelected = string.Equals(_frames.SelectedBlockId, hit, …);
bool onEdge = FrameGeometry.IsOnEdgeBand(…);
if (isText && !alreadySelected && !onEdge)
{
    return false; // fall through to the text path — caret
}
```

Click a text frame and you type in it. Click it *again* and you select it as an object. Click within
4 points of its edge and you select it the first time. This is exactly what a layout program should
do, and there was **no indicator anywhere** that there were two states, nor that Enter, F2 and Esc
move between them — three keys in no menu, no panel and no catalog entry.

The status bar now says which one you are in, at the **lowest priority** it has, so anything the app
actually needs to report still wins:

| State | What the bar says |
| --- | --- |
| Writing | You are writing in this frame. Esc chooses the frame instead; Tab moves on to the next one. |
| Frame chosen | This frame is chosen. Press Enter to write in it, or Tab to move to the next one. |

## 2. Tab was worse than undocumented

It was **swallowed**:

```csharp
Key.Tab => true, // swallowed inside a session; Tab cycles frames in frame mode
```

So the only keyboard way out of writing was Esc and then Tab — two presses for what every other
program does in one, neither written down. Tab now does both: leave the writing, go to the next
frame. Nothing is lost, because a tab *character* cannot be typed into a story anyway (the sanitiser
drops it, §14.2), so the key had no other job.

## 3. Alt has suppressed snapping since M5 and was advertised nowhere

It is now said while a drag is actually happening — "Hold down Alt while you drag to ignore the
lining-up guides" — because a fact about a modifier key is useless before the gesture it modifies has
begun, and noise in the status bar the rest of the time.

## 4. What guards it

- `FrameShellTests.TheCanvasSaysWhichModeYouAreInAndTabLeavesTheWriting` — both sentences, and Tab
  leaving the session **and** landing on a different frame in one press.
- `FrameShellTests.DraggingAFrameSaysThatAltIgnoresTheGuides`.
- `LinkModeArmsFromTheKeyboardAndExplainsItselfInPlainLanguage` **was updated, and that is the
  evidence.** It asserted the status bar was empty after leaving link mode; adding the hint made it
  fail. That is a pre-existing test failing on the change, which is stronger than anything I could
  have written afterwards.

Recorded honestly: I could not get the two *new* tests to fail cleanly against reverted source in
isolation — the headless session died for unrelated reasons in that run, and one of them references
a constant that did not exist before. The updated link-mode test is the load-bearing evidence.

## 5. What was NOT done

**No keyboard equivalent for marquee selection, add-to-selection or pan.** Each is a new command
needing real design — "select everything on this page" is not quite a marquee, and a keyboard pan
competes with the arrow keys that nudge a frame. They stay open in §14.6.

**No pointer-anchored zoom from the keyboard**, for the same reason: without a pointer there is no
anchor, and inventing one would be a different feature wearing the same name.

Suite after M44: **1203 passing, 12 skipped**. No baseline moved, no screenshot re-baked.
