# M36 — Reading files, and naming things

**Delivered 2026-08-07.** Three §14.2/§14.3 items about the app misreading what it was given.

---

## 1. A Windows-1252 CSV lost its accented names

`CsvTableReader` read every BOM-less file as UTF-8. An older Excel "Save as CSV" on Windows writes
Windows-1252 with **no byte-order mark**, and decoding that as UTF-8 replaces every accented letter
with U+FFFD. Silently. The brother's name is simply wrong in the address book from then on.

UTF-8 is now tried with a **throwing** decoder rather than a replacing one, and the exception is the
signal to fall back. That is what makes the fallback safe: a file that really is UTF-8 cannot
produce a `DecoderFallbackException`, so the fallback can only be reached by a file that is not one.

Windows-1252 rather than Latin-1 as the fallback, because it is what Excel actually writes, with
Latin-1 behind it for any runtime where the code-pages provider is unavailable — they agree on every
byte that matters for a name and differ only in the punctuation range.

## 2. Header hints matched inside other words — and then still matched

The mapping screen guesses which column is which from its header. `Matches` used a bare
`Contains`, and the misfires were all in the same direction: **"Member Number"** scored as Name on
the hint `"member"`, **"Mailing address"** as Email on `"mail"`, **"Member No."** as Phone on
`"number"`.

Two changes were needed, and the first alone was not enough:

- **Word boundaries.** `"mail"` no longer matches inside `"Mailing"`. `"Member Name"`, `"e-mail"`
  and `"Phone#"` all still match, because a boundary here means "not flanked by a letter or digit".
- **Narrower hints.** Word boundaries do not help with `"Member Number"`, where `"member"` genuinely
  *is* a whole word — it is just a word that appears in headers naming something else. `"member"`
  and `"number"` are gone as hints; `"member name"`, `"phone number"` and `"telephone number"`
  replace them. The hint has to be the thing itself.

The screen presents its guesses as *"We guessed these. Change any that are wrong."*, so a wrong
guess is correctable — but only by somebody who notices, and the whole point of guessing is that
they should not have to check every column.

## 3. The right-click menu used static command names

Three commands are named for the state they are in — the two picture ones since M18, Save since
M24 — and `ActionCatalog.TitleFor` is where that lives. The action panel has always called it; the
right-click flyout called `offer.Action.Title` instead.

So right-clicking a frame that already held a photograph offered **"Put a picture here…"**.

---

## 4. What guards it

- `TableReaderTests.ABomlessWindows1252FileKeepsItsAccentedNames` — asserts the names survive and
  that no U+FFFD appears. Confirmed to fail against the old reader.
- `TableReaderTests.AUtf8FileIsStillReadAsUtf8` — the fallback must not capture a genuine UTF-8
  file.
- `RosterImportSessionTests.AHeaderDoesNotMatchAHintBuriedInsideAnotherWord` (3 cases) and
  `ARealHeaderStillMatchesItsField` (4) — both directions, because narrowing hints until nothing
  matches would also pass the first test.
- The flyout change is one call swapped for another that the panel beside it already used.

Suite after M36: **1184 passing, 12 skipped**. No snapshot baseline moved.
