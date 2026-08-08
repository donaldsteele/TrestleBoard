# M42 — Four dialogs that would not say what they meant

**Delivered 2026-08-08.** Closes three §14.4 dialog findings and the §14.3 "two disability models"
one, plus a styling bug they exposed.

---

## 1. "Stop" did not say stop what

The import wizard's footer read `Stop  Back  Next`. **Stop the step? The import? The program?** The
automation name had said it in full since M12 — "Stop importing and change nothing" — and the
visible label said a single ambiguous word. It now reads **"Stop the import"**.

## 2. "Next" looked like the button that abandons the work

`Next` is the button the user is meant to press, and it was styled identically to `Stop`. It is now
`IsDefault`, which is both how the primary treatment is chosen (M16) and what makes Enter mean
"carry on" — which is what somebody who has just answered a question expects it to mean.

### The bug that hid under it

Setting `IsDefault` did not work at first, and the reason is worth recording: **`Button.action` was
declared after `Button[IsDefault=True]` in `Controls.axaml`, and later styles win.** So from M37
onwards, every button that was *both* default and marked with `Tokens.Action()` silently came out
looking ordinary. The officers-sync dialog's "Update the table" had been in that state for
milestones — visible in the committed screenshot, and nobody looked.

The `[IsDefault=True]` block now sits last. The way onward in a dialog is the way onward whether or
not somebody also asked for the action affordance.

## 3. The size slider had no ends

Settings showed `100%` beside a bare track. Neither end meant anything: a number over an unlabelled
track does not tell somebody what happens if they drag it right. It now has tick marks and two
labels — **"Normal (100%)"** and **"Twice as big (200%)"** — worded about the buttons and menus
rather than about percentages.

## 4. The one control that was unavailable and would not say why

M11's rule is that nothing in this app becomes unavailable without saying why, in plain English. It
was enforced for the action panel and the menu bar, and **stopped at the dialog's edge**. The
officers-sync dialog has a tick box per row, and the row where two brothers claim one office starts
disabled — correctly, because M19 refuses to decide that for the user — with no explanation
anywhere.

The label itself now carries the reason while it cannot be ticked:

| State | Label |
| --- | --- |
| No name chosen yet | **Choose one of the names above first** |
| A name chosen | **Put the one I chose into the Senior Warden row** |

The reason is *read* rather than inferred from a grey box, and `HelpText` carries it for a screen
reader exactly as the menu bar has since M11.

## 5. What guards it

- `PeopleShellTests.TheImportWizardSaysWhatStopStopsAndMarksTheWayOnward` — the label, the default
  flag, **and** that the default button actually wears the primary theme. That last assertion is
  what catches the style-order bug, and it was verified failing with the reorder reverted.
- `PeopleShellTests.TheOfficersSyncCheckboxSaysWhyItCannotBeTickedYet` — builds an ambiguous office
  from two fictional members and asserts both the label and the HelpText.
- `SettingsTests.TheSizeSliderSaysWhatItsEndsMean` — both end labels, and that the track has ticks.

All three verified failing against the pre-M42 dialogs.

## 6. What was NOT done

The wizard's own "Cancel beside Save it" problem (§14.4) is untouched. It is the same family, but it
needs the grid-versus-wizard confirmation inconsistency from §14.2 settled with it rather than
separately.

Six screenshots re-baked. Suite after M42: **1199 passing, 12 skipped**. No snapshot baseline moved.
