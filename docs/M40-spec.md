# M40 — The address book stops losing edits quietly

**Delivered 2026-08-08.** Closes the People-window entry in §14.4, all three parts of it.

---

## 1. The silent loss

The People window saves one person at a time, and that is deliberate — the window exists for *"look
somebody up and fix their phone number"*, not for browsing a membership. What was wrong was not the
model but what happened when the user walked away from a half-finished edit:

```csharp
private void OnListSelectionChanged()
{
    …
    _adding = false;
    Show(_shown[index]);   // overwrites every field, no question asked
}
```

Type a corrected telephone number, click the next name in the list, and the correction was gone. The
person who lost it had no way to know: the list had done exactly what they clicked on, and nothing
anywhere said a number had just been discarded.

**This is the roster**, so what was being thrown away was a real member's real details — PLAN.md §0
rule 5 — and it was the one place in the app where losing them was silent.

There were three doors out of a dirty form, and all three were quiet: clicking another name, pressing
"Add a person", and closing the window. All three now ask.

## 2. The question

The same three-button question the newsletter asks (M24), in the same words, because it is the same
question about a different thing:

> **You have changed Aaron Placeholder but not saved it yet.**
> If you carry on without saving, those changes will be gone.
>
> `Go back`  `Do not save`  `Save this person`

"Save this person" is the default and is worded identically to the button on the form, so the answer
and the act are named the same thing.

Two details that are easy to get wrong:

- **The list is put back before the question appears.** Otherwise the window sits showing one
  person's name highlighted over another person's details while the user reads the dialog.
- **"Save this person" that fails does not carry the user onwards.** `Save()` now returns a bool: an
  empty name or an unreadable birthday is refused with a reason in the status line, and going
  anywhere at that point would discard the very edit the user was just asked to fix.

Dirtiness is one signature string built from all eight fields rather than a field-by-field
comparison, so a field added to this window in future is covered by whoever adds it to `Show` —
there is no second list to keep in step.

## 3. The other two parts of the finding

**The date field had no example.** The birthday field has carried `like 7/4` since M12; the
raised/initiated field carried nothing, though it is the harder of the two to guess — a date with a
year in it, in a window where the other date deliberately has none. It now reads
"The date he was raised or initiated, like 3/14/1998".

**"Remove this person…" looked exactly like "Save this person".** It gets a new `Destructive()`
treatment: a heavier outline in the warning colour, with the label to match. It keeps the `action`
class, so it is still an app-made button and `ActionSurfaceTests` still covers it — the destructive
theme is `BasedOn` the action theme rather than replacing it, and differs only where it should.

Per PLAN.md §6, **colour is not the only signal**: the outline is thicker as well as redder, and the
label's ellipsis already promises a question. `Warning` is a palette token whose contrast on
`Chrome.Background` is machine-checked at ≥4.5 in all three themes.

## 4. What guards it

- `PeopleShellTests.SwitchingPeopleWithAnUnsavedEditAsksFirst` — verified failing against the
  unguarded code, and **at the right assertion**: "Go back" left the form showing person 2.
- `PeopleShellTests.DiscardingAnUnsavedEditLeavesTheAddressBookAlone` — the discard path writes
  nothing.
- `PeopleShellTests.TheRemoveButtonDoesNotLookLikeTheSaveButton` — both are `action`, only one is
  `destructive`, and their themes differ.
- `SelectForTest` was rewritten to click the list rather than set `_selectedId` directly. M40 put a
  question on that path, and a test hook that stepped around it would be testing the wrong door.

`people-window.png` regenerated: fictional fixtures only, no text chunks.

## 5. What was NOT done

- **No autosave-on-switch.** It would remove the question, and it would also write a half-typed
  telephone number into the address book the moment the user's attention moved. The refusal to lose
  work quietly should not become a habit of saving work quietly.
- **The wizard's Cancel-beside-Save-it problem (§14.4) is untouched.** It is the same family of
  finding but a different window, and it needs the grid-vs-wizard inconsistency in §14.2 settled
  with it rather than separately.
- **`Warning` is measured against `Chrome.Background`, not against Fluent's button fill.** The two
  are close enough in all three themes that the ratio only rises, but the machine-checked pair is
  the background, not the button.

Suite after M40: **1195 passing, 12 skipped**. No snapshot baseline moved.
