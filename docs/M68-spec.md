# M68 — The licence, at last

**Delivered 2026-08-08.** PLAN.md §11 M68; closes the §13 item that has been open since M15.

## 1. The decision, and who made it

§13 has carried *"the application has no licence file"* for as long as §13 has existed. M15 did not
invent one, and that was right: a licence is the owner's word, not the machine's. The owner made the
call on 2026-08-08 — **free for non-profit use; profit-making use requires a separate licence** —
and read and signed off PolyForm Noncommercial 1.0.0 as the text that says so.

PolyForm was the candidate PLAN.md named, and it survived contact for one reason: it is written for
exactly this split, by people who write licences, and it names the beneficiaries this app actually
has. Its *Noncommercial Organizations* section grants use to *"any charitable organization,
educational institution, public research organization, public safety or health organization,
environmental protection organization, or government institution … regardless of the source of
funding"*. A lodge, a church and a school are all covered by a sentence nobody here had to draft.

Two alternatives were offered to the owner and declined: a rider forbidding modification (it would
make an off-the-shelf licence non-standard, and a successor committee reasoning about a bespoke
hybrid is worse off than one reading a published one), and a bespoke plain-English licence (easier
to read, legally untested).

## 2. What ships

`LICENSE` at the repository root, in two halves:

- **A plain-language preamble**, in the voice of the app rather than of the profession. Who may use
  it free and by name — a lodge, a church, a charity, a school, a public body, a person at home.
  What needs a separate licence, in the terms a person recognises: *selling it, selling a service
  built on it, or using it in the running of a for-profit business*. Where to ask for one: the
  repository's issue tracker. The owner chose the issue tracker over an email address, so no
  personal address is published in a file that travels with every copy.
- **PolyForm Noncommercial 1.0.0, unchanged**, as published, heading and all.

The preamble comes **first** on purpose, and a test holds that order. The reader this application is
written for is an elderly volunteer, and meeting `## Acceptance` cold is how a licence goes unread.

Two carve-outs are stated in the preamble because they are not ours to grant: the twenty bundled
typefaces remain under the SIL Open Font License 1.1 whatever anyone does with TrestleBoard, and
nothing here grants any right in the names, arms or symbols of Indian Land Masonic Lodge 414 or of
any Grand Lodge.

## 3. How it reaches somebody who did not clone the repository

This is the part M14 already learned the hard way, and the reason the milestone is not just a file.

PolyForm's *Notices* section requires that anyone who gets a copy of the software also gets these
terms, together with any plain-text line beginning `Required Notice:`. A `LICENSE` sitting in a git
repository satisfies that for people who read GitHub and nobody else. The committee member who
installs a `.exe` from a release has never seen it.

So the root `LICENSE` is an `<EmbeddedResource>` in `TrestleBoard.App` —

```xml
<EmbeddedResource Include="..\..\LICENSE" Link="LICENSE" LogicalName="TrestleBoard.App.LICENSE" />
```

— exactly as `THIRD-PARTY-FONTS.txt` is one in `TrestleBoard.Layout` (M14, `PLAN.md` §13's
already-closed OFL item). It therefore travels inside the assembly, into all four RIDs, through
`vpk pack`, with no step in `release.yml` to forget. `AppLicence.ReadText()`
(`src/TrestleBoard.App/AppLicence.cs`) is the only reader.

## 4. The two places the user meets it

- **Help → "Licence"** — a new `ActionId.Licence` (`help.licence`), through all of M11's surfaces:
  the id, the catalog entry with its plain description, the always-available arm of `Evaluate`, the
  menu item, the runner handler, and an on-the-record entry in `ActionIcons.WithoutAnIcon` ("read
  once, never hunted for"). No shortcut, so no `KeyboardMap` row. It reuses
  `ShowScrollingTextAsync`, whose doc comment has said *"the font licences, and nothing else yet"*
  since M14; that comment is now out of date in the good way.
- **Help → "About TrestleBoard"** names it in two sentences — free for lodges, churches, charities
  and personal use; making money with it needs a separate licence; the whole thing is under Help.

**"Licence" and "Fonts and licences" stay two separate commands, and "Licence" sits above.** They
are two grants from two different people, and running them together would blur which permission
comes from where. The menu order says which one governs TrestleBoard itself.

The About text moved out of the dialog call into `MainWindow.AboutText()` so a test can read it.
M68's acceptance requires About to name the licence, and a claim nothing checks is a claim that
rots.

## 5. What guards it

`tests/App.HeadlessTests/LicenceTests.cs` — nine tests, none of which opens a window:

- `TheLicenceShipsInsideTheAssemblyRatherThanBesideTheRepository` — the resource is in the manifest.
  This is the M14 gate applied one level up.
- `TheEmbeddedLicenceIsTheFileAtTheRootOfTheRepository` — the shipped copy and the file on disk are
  the same text. The failure this prevents is silent: editing `LICENSE` and shipping the old bytes.
- `TheLicenceCarriesPolyFormsTextUnchanged` — three load-bearing sentences of PolyForm's, including
  the charitable-organization grant. If somebody "tidies" their words, this says so.
- `TheLicenceSaysInPlainLanguageWhoMayUseItBeforeItSaysItInLegalLanguage` — the preamble precedes
  the formal heading. §6 is a property of the licence too.
- `TheRequiredNoticeTravelsWithTheSoftware` — PolyForm §Notices, machine-checked.
- `SomebodyWhoWantsToPayForACommercialLicenceIsToldWhereToAsk`.
- `TheAboutWindowNamesTheLicenceAndPointsAtTheWholeThing`.
- `TheAppsLicenceAndTheFontsLicencesAreSeparateCommands`.
- `TheReadmeGrantsThePermissionTheLicenceGrants` — and, in the negative, that the README never goes
  back to *"no licence file yet"* or to granting no permission at all.

The existing M11 surface gates did their job unprompted: `MenuIndexTests`, `ActionSurfaceTests`,
`IconTests` and `ActionCatalogTests` all cover `help.licence` the moment the id exists.

## 6. What was NOT done

- **No change to `release.yml`.** The embedded resource is the delivery mechanism; a copy step would
  be a second thing to keep in sync. This is M14's answer, re-used deliberately.
- **The font licences were not merged into the new window.** See §4.
- **No `LICENSE` header comment added to source files.** PolyForm does not ask for per-file notices,
  and 300-odd files each carrying a banner is noise the next reader has to skip.
- **`docs/M15-spec.md` is not edited.** Spec documents are the record of what their milestone
  decided; M15 correctly documented an absence that was real at the time. §13 is where the status
  lives, and §13 is where the strike-through went.

Suite after M68: **1226 passing, 12 skipped** (nine new). No baseline moved, no screenshot re-baked,
no file in `Layout`/`Rendering`/`Export.Pdf` opened.
