# M55 — Who gets it, and who we've lost

**Delivered 2026-08-08.** PLAN.md §11 M55. The deepest personal-data change in the batch (§0 rule 7).

## 1. What the owner decided, and where it differs from PLAN.md

PLAN.md's deliverables named **three** statuses — active, moved away, and passed. The owner chose
**two**: active, and passed to the Celestial Lodge.

That is coherent rather than a cut, and the reason is groups. "Moved away" was only ever a way of
saying "stop sending him things"; with a `Gets a printed copy` group in the same milestone, a
brother who moves is taken off that group and stays an ordinary member. A status for it would have
been a second way to say one thing, and the two would eventually disagree.

The owner also chose: the birthday list changes **behind the existing diff dialog, never silently**,
and a memorial is **offered once and never inserted uninvited**.

## 2. §14.5 item 3 was already done

M55's deliverables include "the CSV encoding detection §14.5 item 3 left undone". **It was not
undone.** M36 delivered it in commit `4aaf9b8`: `CsvTableReader.ReadStrictUtf8OrFallBack` tries
strict UTF-8, falls back to Windows-1252, then Latin-1, and two tests
(`ABomlessWindows1252FileKeepsItsAccentedNames`, `AUtf8FileIsStillReadAsUtf8`) have covered it since.

PLAN.md §11 M55 and §14.5 item 3 were both stale. They are corrected rather than re-implemented; no
code was written for this deliverable because the code exists and passes.

## 3. Status is a date, not an enum

`Member` gains exactly one field for status: `PassedOn`, a nullable ISO date string. `HasPassed` is
`PassedOn is not null`.

An enum beside the existing `IsActive` would have made **two properties that can disagree**, and the
first disagreement puts a deceased brother back in the birthday list — the single worst error this
product can ship. A date is also the thing the committee actually knows, and it is what a memorial
notice needs. `Normalised()` enforces the one invariant that remains: a brother recorded as passed
is off the rolls, whatever a hand-edited file left in the other field.

`IsActive` keeps its old meaning — *still a member* — because "no longer a member" and "has died"
are different facts and the second is not something to record by un-ticking a box. Every caller now
asks `IsInTheNewsletter` (`IsActive && !HasPassed`) instead of either, because a projection that
checked only one of them is exactly the defect this milestone exists to prevent.

**A correction to something I wrote mid-milestone:** I claimed `IsActive` had never done anything.
That was wrong — `BirthdayRosterProjection` has filtered on it since M13, and my grep had excluded
the very line that proves it. The comment that said so is gone.

## 4. Groups

A free-text list per member, offered as tick boxes rather than typed, because a group that is nearly
spelled right is a brother who quietly stops receiving his newsletter. `MemberGroups` names the
three the app itself reads — `Gets the newsletter by email` (M56), `Gets a printed copy` (M56 counts
them), `Officers` — and `InUse` offers those plus every group the lodge has invented, so a name is
never re-typed.

> **Amended 2026-08-08.** This section originally said M62 would print the printed-copy group's
> labels. **M62 was dropped**: the roster holds no postal address, so labels meant collecting them,
> and this is not a membership management tool. The group is a count, not an addressing list.

`MemberGroups.Members` returns only people who `IsInTheNewsletter`. A deceased brother left in the
printed-copy group would otherwise be posted a newsletter.

## 5. The defect gate 9 caught in under a minute

Adding `Groups` broke **re-import idempotence**, and three existing tests said so immediately.

A record compares each field with `==`, and on an `IReadOnlyList<string>` that is *reference*
equality. Two members with identical group lists in different arrays therefore compared as
different — and `RosterMerge` decides whether anything changed by comparing members, so re-importing
the same spreadsheet reported **every single person as edited**.

`Member.Equals` and `GetHashCode` are now written out by hand, with `SequenceEqual` for the groups
and a raw-text comparison for the overflow bag. Hand-written equality can rot, so
`EveryStoredPropertyIsPartOfBeingTheSameMember` reflects over the type and fails the moment a
property is added without being compared.

## 6. Round trip, and the asymmetry it exposed

The export has written an **Active** column since M12 with no matching `RosterField` and no read
path — so somebody could un-tick a brother in Excel, re-import, and watch the change vanish without
a word. M55 does not repeat that shape: all three new columns go both ways.

- Export gains **Passed on** and **Groups** (semicolon-separated: a CSV round trip through Excel
  quotes commas inconsistently, and a group name with a comma in it is likelier than one with a
  semicolon), and **Active** is renamed **Still a member** to match its tick box.
- `RosterField` gains `StillAMember`, `PassedOn` and `Groups`, so all three appear on the mapping
  screen automatically and all three are read by `RosterMerge.Apply`.
- **`"status"` is deliberately not a hint.** It has belonged to *Raised or initiated* since M12;
  claiming it now would quietly re-point every existing lodge's status column at a different field
  on their next import.
- `FieldValues.ReadYesNo` returns **null** for a word it cannot read, so an unrecognised cell leaves
  the value alone rather than guessing "no" — and "no" here means striking a brother off the rolls.
- `RosterMerge.Describe` names all three, or the review screen would say "changes nothing" beside a
  row about to record that a brother has died.

## 7. The two moments that matter

**The birthday list.** `Candidates` now filters on `IsInTheNewsletter`. The removal arrives at the
existing diff dialog, and the dialog now says **why**: *"He has been recorded as passed to the
Celestial Lodge."* "Taken away" beside a brother's name with no reason is the moment a committee
member wonders whether the program has lost him. Ordinary removals — a birthday that moved out of
this month — carry no reason, because inventing one would teach the user to stop reading them.

**The memorial.** Recording a brother as passed raises a card that says what has *already* happened
before it asks anything: his record is kept, he is out of the birthday list, and nothing on a page
changes without being asked. Then, once: *"Would you like to write a memorial notice for him?"* The
People window has no newsletter, so it records the request and the shell opens M54's memorial with
his name filled in and the date left as a blank. With no cursor anywhere, it says where to start
rather than opening a wizard whose last button cannot do anything.

## 8. Privacy (§0 rules 5 and 7)

Status and group membership are the most sensitive data this app holds: whether a named man is
alive, and which lists he is on. Nothing changed about where it lives — `roster.json` in AppData,
gitignored, the backup ring beside it — and every fixture added here is fictional. The exploration
of this milestone was told not to open a real roster and confirmed none exists on this machine.

The pre-push scan over today's six commits found no lodge data. The only addresses in the diff are
upstream contributor credits inside the SCOWL licence text M52 is required to ship verbatim.

## 9. What guards it

- **`tests/Roster.Tests/MemberStatusAndGroupsTests.cs`** (14): no date means alive; the date *is*
  the status; the two fields cannot disagree; off the rolls is not the same as died; whitespace is
  no date; groups are tidied without being reordered; membership ignores case; a group never hands
  back somebody who has passed; the offered list is known-then-lodge's-own; and five equality tests
  including the reflection guard.
- **`tests/Widgets.Tests/BirthdayRosterProjectionTests.cs`** (+4): the acceptance sentence itself —
  *absent from the projection in the same run that keeps his record* — plus the two removal reasons
  and the case that deliberately has none.
- **`tests/App.HeadlessTests/PassedBrotherShellTests.cs`** (4): the record survives and the groups
  drop him; "Not now" writes nothing; the memorial arrives as ordinary writing in one undo step with
  the date left blank; nowhere-to-write says where to start.
- The three existing idempotence and export-round-trip tests, which caught §5 unprompted.

### Failure-first evidence

Reverting the projection filter to `IsActive` failed
`ABrotherWhoHasPassedLeavesTheListInTheSameRunThatKeepsHisRecord` and `TheListSaysWhyHeIsBeingTakenAway`;
removing the invariant from `Normalised()` failed `AHandEditedFileCannotLeaveHimBothPassedAndOnTheRolls`.
Three named failures, and only those.

## 10. What was NOT done

- **No "moved away" status** — the owner's decision; see §1.
- **No migration code.** There is none in the roster and none was needed: both fields are additive
  with safe defaults, and the overflow bag has carried unknown properties since M12
  (`UnknownPropertiesSurviveARoundTrip`).
- **No group management screen.** Groups are created by typing one on a person and are then offered
  to everybody. A screen for renaming and deleting them is a real feature and not this one.
- **Deleting a member is untouched.** M55's whole point is that nobody has to be deleted to be taken
  off a list.

Suite after M55: **1356 passing, 12 skipped** (22 new). No baseline moved, no screenshot re-baked.
