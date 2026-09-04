# M88 — The address book holds what the secretary holds

**Status:** implemented. Delivered 2026-09-04.

M12 stored seven fields a person types and argued, at length, for the absence of everything else.
The owner reversed that on 2026-09-04, after looking at what the lodge's own member-management
system exports: 112 members and 16 spouses, with a full postal address for every man, three
telephone numbers, a lodge member number, his degree and its date, a Masonic post-nominal and a real
birth date. A book that drops all of it makes the secretary keep a second list, which is the thing
this app exists to stop.

What M12's reasoning was actually protecting was the **form**, not the record. The form is now two
tabs, and the seven fields somebody edits in a hurry are still the first thing they see.

---

## 1. The hard constraint, and why it shaped everything

`OfficersRosterProjection.Fingerprint` hashes `(MemberId, Name, Phone)` per office;
`BirthdayRosterProjection.Fingerprint` hashes `(Id, DisplayName, BirthDay)`. Both are **stored inside
every already-synced `.tboard`**. Widen what either hash sees and every synced table in every
newsletter on the committee's laptops reports itself out of date on the next open — for a change
that alters not one printed character. The lodge would be told its newsletter was stale because the
app learned somebody's ZIP code.

So no field M88 adds may reach a projection. `Widgets.Tests/RosterProjectionInvarianceTests` asserts
it bluntly: set every new property on every member, and both fingerprints and both sets of projected
rows must come back identical.

**The test was weaker than it looked when first written, and that is worth recording.** Breaking the
projection on purpose — feeding `MobilePhone` into `OfficerCandidate` — did *not* fail the
fingerprint test, because every man in the fixture had a `Phone` and the fallback could never fire.
A third member with no telephone number was added for that reason alone, and the deliberate break
then failed on the fingerprint assertion as it should. A guard that cannot fail is not a guard.

## 2. The model — sixteen flat properties

`MemberNumber`, `BirthYear`, `Degree`, `MasonicTitle`, `AddressLine1`, `AddressLine2`, `City`,
`State`, `Zip`, `AddressUndeliverable`, `HomePhone`, `MobilePhone`, `WorkPhone`, `SpouseName`,
`SpouseEmail`, `SpousePhone`. `Member` goes from 14 stored properties to 30.

**Flat, not nested `MailingAddress`/`Phones` records.** A nested record's compiler-generated equality
would have saved sixteen lines in `Member.Equals` and cost a null-parent dance at fourteen write
sites in `RosterMerge.Apply`, plus a second census for every nested type — the property census at
`MemberStatusAndGroupsTests` sees names, and nesting hides the leaves from it.

What replaces the safety nesting would have bought: **`ChangingAnyOneStoredPropertyMakesADifferentMember`**,
which sets each stored property in turn through reflection and demands the two members compare
unequal. The name census says a property was *listed*; this says it is *compared*. With a
hand-written `Equals` and thirty properties, the first is no longer enough — and the symptom of a
missed line is the quiet one: `RosterMerge.Plan` writes a member only when `changed`, so a forgotten
field means an import that reports "nothing changed" and drops the edit.

**Not added, on the record:** first/middle/last/suffix as separate fields. Splitting a name is a
different product; the father-and-son problem it would have solved is solved by the member number.

## 3. Decisions inside the model

**`Phone` did not move.** It is still "the number printed in the newsletter" and still the only
number any projection sees. The three new ones sit beside it, because 24 of the lodge's 112 members
have both a home and a mobile number and one field silently drops one of them. The form says which
is which in a sentence under the box.

**The lodge's file has no `Phone` column at all** — Home, Mobile and Work, and nothing else — so
`RosterMerge.Apply` fills an *empty* `Phone` from `MobilePhone ?? HomePhone` at the end of a row.
Never in `Normalised()`: that fires on load, which would move the officers fingerprint for every
member who holds an office and has no `Phone` today. As an import rule it is a reviewed, counted
change, and `Describe()` names it.

**The degree ladder is a new field, not a widened old one.** `DegreeKind` (raised | initiated) says
which ceremony `DegreeDate` records; `Degree` (Entered Apprentice | Fellowcraft | Master Mason) says
how far a man has come. They are different questions and the lodge keeps the second. `Normalised()`
migrates `raised` ⇒ Master Mason and clears the old field — raising *is* the Master Mason degree, so
nothing is invented.

**`initiated` is deliberately not migrated.** A man initiated in 1998 is almost certainly a Master
Mason today; writing "Entered Apprentice" on his card would be the app making a claim about a real
brother that nobody made. It stays as it is and corrects itself on the next import from the lodge's
own list, which carries the degree for everybody.

**`BirthYear` is stored and never printed.** `HasBirthday` and `BirthdayText` are untouched, so the
newsletter still gets a month and a day by construction rather than by care. Four guards:
`BirthdayText` unit tests, the fingerprint invariance above, screenshot alt text that says the year
is stored and not printed, and a source census —
`NothingThatCanReachAPageEvenMentionsTheBirthYear` — asserting the property's name appears in no
`.cs` file under Widgets, Editing, Rendering or PdfPages.

**A spouse is fields on her husband's card, never a person in the book.** The guarantee is
structural: there is no `Member` row for her, so there is nothing for a group, a projection or M56's
BCC line to find. `ASpouseIsNotAPersonInTheBook` writes the corollary down.

## 4. Import

**`ColumnGuesser` now prefers the longest matching hint**, with `RosterFieldInfo.All` order as the
tie-break. First-field-wins was right at ten fields and stopped being right the moment the lodge's
own headers arrived: "Mobile Phone" matched the telephone's older hint `mobile` before the mobile
field was reached, "Address Undeliverable" matched `address`, and — a misfire that predates this
milestone — "Highest Degree Date" matched `degree` and became the degree *kind*. Every recorded
collision stays shut: `member`, `number`, `mail` and `status` are still nobody's hint.

`SecretarysFileTests.EveryColumnOfTheLodgesOwnFileIsGuessedRight` is the regression table, one row
per column of the real export.

**One hint was added for our own file.** "Mobile telephone" is what M88's export writes, and without
it that header matched `mobile` (six letters) and took the printed-telephone field, leaving the
lodge's real Phone column unmapped on a re-import of our own file. Same shape of bug M12 recorded
for "Raised or initiated", found the same way: a round trip.

**Member number is the merge's second match key** — TrestleBoard ID → member number → email → name.
Above email deliberately: an email can be shared between a husband and a wife or a father and a son;
a member number is issued once, to one man. It matches only when exactly one member carries it.

**And it is the guard that fixes a real defect.** `NameMatching.Normalise` strips Jr/Sr/II/III, so
before M88 a son's row matched his father exactly, overwrote him, and the book came out one man
short with nothing said. Two such pairs are in this lodge's list. The rule: the name step may not
match a member whose non-empty number differs from a non-empty incoming number, and the near-match
question is not raised for such a pair either — that question is already answered.
`NameMatching` itself is unchanged; its suffix-stripping was right for fuzzy work, and the defect
was fuzzy comparison doing an exact key's job.

**Two shapes of the lodge's file the app now understands, so no conversion script exists:**

- **Repeated rows.** The report emits one row per birthday item, so a man appears two or three
  times. Same member number, so the later rows report `Unchanged`.
- **Spouse rows.** `RosterField.RowKind` reads the column that says what a row is; a row marked
  "Spouse of &lt;name&gt; #&lt;number&gt;" never becomes a member, and its name, email and telephone
  land on that member's card. New `RowOutcome.Spouse`, counted out loud on the review screen — *"16
  rows are spouses, not members."* — with a plain sentence for one whose number names nobody we hold.

**The round-trip test found four fields the export wrote and nothing could read back**: Notes, the
birth year, the undelivered flag, and the two spouse contact columns (the apostrophe in "Spouse's
email" defeated the hint). `EveryExportedColumnComesBackToTheSameField` sets all thirty properties,
writes the file, reads it with nothing but the guesser, and demands the merge see no change. That
test is the reason `RosterField.Notes` and `RosterField.BirthYear` exist.

## 5. Export

Twenty-eight columns, **append-only**: the first eleven keep their exact headers and positions, so an
older TrestleBoard still reads the file and gate 9's index-based edits still mean what they meant.

`Headers` and the eleven hand-indexed `Write` calls became one
`(string Header, Func<Member,string> Value)[]`. At eleven columns two parallel lists were survivable;
at twenty-eight, drift between a header and the value under it is a certainty. Every cell is still
forced to text.

## 6. The People window

**The refactor came first, alone.** Seven parallel lists — declaration, layout, search, dirty
signature, `Show`, `Clear`, `Save` — none checked by anything. Sixteen new fields times seven is
about a hundred hand-kept edit points, and the failure is a box the user types into that quietly is
not saved. They are now one `FormField` table, and `PeopleFormTests.EveryTextFieldOnAMemberCanBeTypedOnThisForm`
holds the form to the model: every stored `string` property has a box, or is on a short exclusion
list with a written reason.

That test immediately earned itself. **`Member.Notes` had been stored since M12 with no control
anywhere in the app** — a field the committee could receive from a spreadsheet and never see. It has
a box now.

**Tab 1 "The basics"** is M12's form in M12's order, plus the birth year under the birthday and the
sentence about which telephone gets printed. **Tab 2 "Address and more"** holds the member number,
the post-nominal, the address, the undelivered flag, the three telephones, the spouse fields and
Notes.

Four rules that tabs make possible to get wrong, each with a test:

1. The dirty signature spans both tabs — an address typed and abandoned is an unsaved edit.
2. Saving writes both tabs, whichever is showing.
3. A refusal brings the offending field's tab forward *before* focusing it. Otherwise the caret goes
   somewhere the reader cannot see and a screen reader announces an unreachable box.
4. Switching tabs is not an edit.

**The accessibility walk only sees the selected tab.** `AccessibilityTests.EveryWindow()` — reused by
the icon and contrast walks — yields a second `PeopleWindow` with tab 2 selected. Without it, every
control this milestone added would have shipped unaudited. It is the easiest thing here to miss.

The window grew 1080×720 → 1160×820 with `MinWidth`/`MinHeight` floors, and the form column is a
grid rather than a stack so the tab body takes the height that is left. Stacked, it took only its
natural height: the first screenshot showed fields cut off mid-label with empty window beneath them.

## 7. Compatibility

`RosterBook.CurrentSchemaVersion` is 2. There is still no migration switch and there should not be —
a version-1 file loads with the new fields simply absent, which is the truth about that book. What
the number buys is the other direction: `RosterStore.Load` reports
`RosterLoadState.MadeByANewerTrestleBoard`, so a committee running two versions can be told which
laptop is behind instead of noticing a column that looks empty. Nothing is refused; the book is real
and every unknown property rides through `ExtraProperties` untouched.
`ABookFromANewerTrestleBoardComesBackUnharmed` proves the value survives, not just the key.

The blast radius of a wrong mapping grew — 112 rows times twenty fields — so the review preview line
stopped being `Name — Birthday — Phone` (which showed a blank telephone for every row of the lodge's
own file, reading as data loss) and became name, birthday, whatever number the row carries, and the
town. The Done screen now names the way back: *"Your previous address book was kept…"* The net
itself is unchanged; it was simply never mentioned where somebody would need it.

## 8. Fixtures, screenshots and the real file

`tests/Roster.Tests/Fixtures/members-full.csv` is new: **fictional people, the lodge's real column
headers.** The headers are what the guesser must get right and a header is not personal data; the
rows are, and they stay in gitignored `tmp/`. The fixture also reproduces the two shapes an ordinary
lodge list does not have — repeated rows and spouse rows.

Three of the ten screenshot people gained an address, a mobile, a member number and a spouse, so the
new tab photographs as something rather than as empty boxes. `people-window` and `import-columns`
were re-shot; `people-address` is new, and is the only image that shows what this milestone is.

**No conversion script ships.** The mapping knowledge lives in `RosterFieldInfo` and is
regression-tested by the fixture. `tmp/MemberReport.xlsx` stays gitignored and no test reads it.

## 9. Open items

- **`RosterPrivacyTests.NoRosterFileShipsAnywhereInTheRepository` fails locally** while a generated
  `Lodge-address-book-*.xlsx` sits in `tmp/` held open by another process. That is the test doing its
  job — a roster-shaped file inside the working tree — and it clears as soon as the file is closed
  and removed. Nothing is committed either way: `tmp/` is gitignored.
- **The keyboard-only pass over the two-tab form has not been done by a person.** Every control is
  reachable by construction and the headless suite drives both tabs, but M12's equivalent claim is
  still outstanding in `docs/accessibility-test-script.md` and this one joins it.
- **`SortName` still has no box.** It is stored, it orders the list, and nothing in the app can set
  it. Excluded from the completeness gate with that reason written down rather than quietly skipped.
