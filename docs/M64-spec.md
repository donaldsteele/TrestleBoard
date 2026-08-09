# M64 — Pack it up for my successor

**Delivered 2026-08-09.** PLAN.md §11 M64.

## 1. What it is for

Committee turnover is the existential risk for a volunteer lodge. M57 lets one template be handed
on. Everything else the app has accumulated — the address book and its ten backups, the templates,
the phrase shelf, the personal dictionary, the settings — has until now died with the old computer,
along with the several evenings of typing inside it.

Two commands: **Pack everything up for my successor…** writes one file; **Bring in a predecessor's
pack…** reads it back on the new machine, item by item, asking before it replaces anything.

## 2. One file, and the `.tboard` discipline repeated

`.tbpack` is a zip with a `manifest.json` and one folder per store. It carries what `.tboard`
carries and for the same reasons: a format name, a format version, a minimum reader version, a
migration chain in front of the deserialize, fixed entry timestamps and sorted entry order so the
same stores pack to the same bytes, and temp-then-rename with an fsync on the way out.

The migration chain is **empty at M64 and exists anyway**. A format that gains its migration story
later has already shipped files nobody can upgrade — and a pack is precisely the file most likely
to be opened years after it was written, by an app nobody has written yet.

Not merged with `TboardContainer`: a newsletter has three known JSON parts, a pack has an open-ended
set of opaque store files, and one class doing both would be a switch on which it is. What *was*
shared is `VersionField`, the "read a semver without trusting it to be one" guard, which both
formats need and which existed only inside `MigrationRunner` — the caller passes its own message,
because "this newsletter file is damaged" is the wrong thing to say to somebody opening a pack.

## 3. The folder names are the interface

```
manifest.json
roster/roster.json
roster/roster-backups/roster-20260101-000000-000.roster.bak.json
templates/stated-communication.tboard
templates/stated-communication.json
phrases/phrases.json
dictionary/personal-dictionary.txt
settings/settings.json
```

The first path segment is the part id, which is what makes a partial restore possible without the
reader understanding any of the files. It also makes the pack **legible to somebody who unzips it
with no TrestleBoard at all** — which is the only real insurance a volunteer lodge has if this app
ever stops being maintained. `roster/roster.json` is a readable address book whatever happens to
the program.

Both files of each template travel: the `.tboard` and the sidecar holding the name the user typed.
Taking one without the other hands the successor a shelf of layouts called `stated-communication`
instead of "Stated Communication".

## 4. Store bytes are copied, never re-serialized

A pack round-trips `roster.json` byte-for-byte. That is the acceptance test and also the honest
thing: the pack is a removal van, not an editor, and a pack step that re-wrote the roster would be
a pack step that could corrupt it. It also means a pack written by an older TrestleBoard hands the
newer one a file that the newer one's *own* store migrations then upgrade on load — exactly as
opening an old newsletter does. The pack format's version and each store's version are separate
things, and neither has to know about the other.

## 5. The one restore rule

> **The pack's files are written over the same paths, and nothing the pack does not carry is ever
> deleted.**

That single sentence is the whole policy, which is what makes it explainable to the person doing
it. A restore that also tidied up — removing templates the pack lacks, trimming the backup ring —
would be a restore that quietly destroyed the receiving committee's own work while calling itself a
copy. The consequences fall out of the rule rather than being decided one by one: templates merge,
same-named ones are replaced, the receiving committee's own layouts survive, and a clean machine
ends up byte-for-byte identical because there was nothing there to survive.

Each file lands **whole or not at all** — written to a temp name and renamed over it, the
discipline `TboardContainer` and `RosterStore` already use. A disk that fills up half way through
writing a restored `roster.json` would otherwise replace a good address book with a truncated one,
which is the worst thing this milestone could do. What is deliberately *not* promised is that a part
is all-or-nothing: if the fourth of six templates fails, the three before it stay, because undoing
them would mean deleting files and the one rule is that this code never deletes. The part is named
in the failure report instead.

One safety net on top of the rule: whenever the roster is among the chosen parts, the roster that is
here now is copied into **its own backup ring** first. Somebody restoring a predecessor's address
book onto a computer that already had one has almost certainly done what they meant to — but
"almost" is not good enough for the only copy of a lodge's membership, and the ring is already this
app's answer for exactly that. `RosterStore.Backup` became public for it.

## 6. Nothing that would replace something is ticked

The restore window lists only the parts the pack actually carries, in the order to ask about them —
the irreplaceable first, the merely convenient last, so somebody who stops reading half way down has
still taken the address book. Each row says three things: what it is, what the pack has of it, and,
only where there is something to lose, what is here now and what would happen to it.

**Rows that would replace something start unticked.** A successor is meeting this window on their
first day, holding a file they cannot see inside, and the difference between "add" and "replace" is
the difference between a good afternoon and losing the lodge's address book. Taking is one click;
replacing is one click and a decision.

The consequence is written as a consequence — "You already have 12 people here. Taking this replaces
them" — rather than a warning symbol, because a symbol is a thing to click past.

## 7. A pack arrives from outside

A pack reaches the successor by email, and a zip entry name is a string somebody else chose:
`templates/../../../.ssh/authorized_keys` is a valid one.

`SuccessorPackService.LocalPathFor` is the only place an entry becomes a path, and it is the
security boundary of the milestone. The four single-file stores are matched by **exact entry name**,
so they cannot be redirected at all. The two directory stores take a single file name — anything
with a separator, a drive letter or a `..` is **refused rather than sanitised**, because a pack has
no reason to contain a nested path, so one that does is either damaged or hostile, and quietly
flattening it would be inventing a file the sender did not send. The joined path is then checked to
have actually landed inside the folder. An entry with nowhere to go is skipped; the honest entries
beside it still land.

A part id this build has never heard of also has nowhere to go — and is **kept in the file**.
Opening a pack must never be a way of shrinking it: the successor may yet install the newer
TrestleBoard that understands the part.

## 8. A new action group

`ActionGroup.Everything` — "Everything on this computer" — rather than `Newsletter` or `People`. A
pack is not a newsletter and it is much more than the address book, and M63's help window prints the
group beside every answer, so "Pack everything up — This newsletter" would have been the app telling
somebody the wrong thing about its most consequential file. Like `People` it never reaches the
action panel: nothing here is about what is selected on the page.

Both commands are declared **always available**. Both are most likely to be reached on a computer
that has never had a newsletter open — the successor's, on their first afternoon. Neither has an
icon (a box glyph would say "archive", a thing you put away; this is a thing you hand to somebody)
and neither has a shortcut, being reached about twice a decade. They sit in their own group at the
bottom of the File menu, above Exit.

## 9. Privacy (§0 rule 7)

The plan calls this "the single most concentrated personal-data artifact the app produces", and it
is: the address book, its ten backups, the templates with the officers in them, and the phrase shelf
with its memorials, in one email attachment.

- **User-chosen path only.** No default location, ever — the save dialog's reasoning from roster
  export and M57, applied to a much larger file.
- **The confirmation says what is in it before it is written,** in as many words: members' names,
  birthdays, telephone numbers and email addresses, give it only to the person taking over, keep it
  off anything shared. Somebody who emails this to the wrong person has emailed the lodge's
  membership to the wrong person, and that sentence is the last thing standing between them.
- **The manifest counts and never names.** `Summary` is the one free-text field in the format and it
  holds "84 people", never a person. `TheManifestCountsAndNeverNames` holds it.
- **`*.tbpack` is gitignored** — it already was, added at M52 in anticipation.
- Every fixture in both test suites is fictional, and the App tests each run against a temporary
  app-state root of their own, so no test can read the real roster even on a maintainer's machine.

## 10. What guards it

`Core.Tests/SuccessorPackTests` — the container, with no app and no Avalonia:

| Test | Holds |
| --- | --- |
| `EveryByteThatGoesInComesBackOut`, `BytesThatAreNotTextSurviveUnchanged` | it is a removal van |
| `PackingTheSameThingTwiceProducesTheSameFile` | deterministic bytes |
| `APartCanBeTakenOutWithoutReadingAnythingElse` | the folder names are the interface |
| `ANewsletterIsNotAPack`, `AZipThatIsNotOursSaysSoRatherThanThrowingSomethingRaw`, `ATruncatedManifestIsADamagedPackAndNotAnUnhandledException` | refusals are sentences |
| `APackFromANewerTrestleBoardAsksForAnUpdate`, `AVersionWithNoUpgradeStepIsRefused`, `AVersionThatIsNotOneIsADamagedPack` | the version gates |
| `APartFromTheFutureIsCarriedRatherThanDropped` | opening never shrinks a pack |
| `EveryPartHasATitleAndASentence`, `TheAddressBookIsAskedAboutFirstAndTheSettingsLast` | the words and the order |

`App.HeadlessTests/SuccessorPackTests` — the real computer:

| Test | Holds |
| --- | --- |
| `EveryStoreOnThisComputerGoesIntoThePack` | all five, both template files, the whole ring |
| `AMissingStoreIsLeftOutRatherThanFailingTheWholePack` | four of five beats an error message |
| `ACleanComputerEndsUpWithEveryStoreByteForByte` | **the acceptance, word for word** |
| `TakingOnlyTheAddressBookTouchesNothingElse` | the other half of the acceptance |
| `ATemplateOfMyOwnSurvivesSomebodyElsesPack` | the one restore rule |
| `TheAddressBookThatIsHereIsKeptBeforeItIsReplaced` | the ring gets a copy first |
| `RestoringLeavesNoHalfWrittenFilesBehind` | temp-then-rename, and nothing littered |
| `AnEntryThatIsNotOneOfOursGoesNowhere` (10 cases), `TheEntriesWeDoKnowLandExactlyWhereTheyShould`, `AHostileEntryDoesNotStopTheHonestOnes` | the security boundary |
| `NothingThatWouldReplaceSomethingIsTickedToStartWith`, `TheWindowSaysHowMuchOfWhatIsHereWouldGo` | the window's promise |
| `OneComputerPacksUpAndAnotherBringsItIn`, `TakingNothingChangesNothing` | the whole thing through the shell |
| `HandingOverIsNeverRefused` | always available |

`docs/accessibility-test-script.md` gains §17 — nine manual screen-reader steps, including that all
three facts about a row must arrive on the **tick box itself** (a screen reader lands on the box, and
anything said only in the text beside it is something the person will never hear), and that the
privacy sentence must be read out. `BringInPackWindow` joins the `AccessibilityTests` walk, and
`bring-in-a-pack` joins the screenshot harness with `MilestoneGate` naming M64.

## 11. What was NOT done

- **Recovery snapshots are not packed.** They are the app's own crash state, minutes old and about a
  document the successor does not have. Packing them would be packing a half-written newsletter.
- **No merging inside a store.** The dictionary is replaced, not unioned; the phrase shelf is
  replaced, not appended. One rule that holds everywhere beats five clever ones, and the window says
  plainly which rows replace something.
- **No encryption or password.** The pack is as protected as the folder it sits in, which is exactly
  as protected as `roster.json` already is. A password on a file handed over once a decade is a
  password nobody will have when it matters, and the honest instruction — hand it over directly,
  keep it off anything shared — is in the confirmation.
- **No automatic pack on a schedule.** This is not a backup feature. Calling it one would invite
  somebody to rely on it, and the thing that protects the roster day to day is the ring.
- **The ring is not trimmed after a restore.** Restoring the roster can leave more than ten backups
  in the folder for a while; the next roster save trims it. Deleting somebody's backups as part of
  restoring their backups would break the one rule.
