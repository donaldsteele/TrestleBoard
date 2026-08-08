# M56 — Send it to the members

**Delivered 2026-08-08.** PLAN.md §11 M56. Depends on M55's groups, which is why it runs after it.

## 1. What was left outside the app

The month used to end with a PDF on a disk. Composing the email — finding the file, remembering who
gets it, typing sixty addresses — was the largest remaining out-of-app task and the most error-prone
of them: a missed brother nobody notices, or sixty addresses in a To: line that everybody notices.

## 2. BCC is the design, not a default

**There is no code path in `MailHandoff` that puts a member's address anywhere but BCC.** Not a
default that a future edit could flip: no `to=`, no `cc=`, and a test that walks the finished URI
and fails if any address appears before the `&bcc=` parameter begins.

Sixty brothers' addresses in a To: line is a disclosure to sixty people who never agreed to it, and
it cannot be taken back. PLAN.md §0 rule 7 names it in as many words. It is also the single most
likely way this feature could hurt somebody, which is why it gets the strongest test in the file.

## 3. No SMTP, no accounts, no network

This app has no business holding a committee member's email password, and a lodge that changes mail
provider must not have to change its newsletter program. What an offline app can honestly do is fill
in the message and hand it to the program they already use — the same reasoning as M53's print
hand-off, one layer up.

## 4. Degrading honestly, three ways

- **The link opens.** The mail program appears with the BCC line and subject filled in.
- **The link would be too long.** `BuildUri` returns null above a conservative 1,800 characters and
  the addresses go to the clipboard in batches of 25 instead. There is no standard `mailto:` length
  limit; what there is, is mail clients that **truncate silently** — and a truncated BCC list sends
  the newsletter to some of the lodge and tells nobody. Refusing the link is the safe side to be
  wrong on, because the fallback works every time.
- **Nothing answers.** Same clipboard path, different sentence saying why.

Both fallbacks tell the user to paste into the **blind copy** line, spelled out, because the whole
protection is lost if they paste into To:.

## 5. mailto: cannot attach a file

No mail program accepts an attachment from a link, for the obvious reason that a link on a web page
should not be able to post your documents. So the message body ends with a line naming the file:
*"Before you send this: attach the file named September 2026.pdf."* Without it, sixty people receive
an email about a newsletter that is not there.

## 6. The printed-copy group

Every message the feature produces also says how many brethren still need a printed copy, when there
are any. That is the other half of M55's groups, and it is what connects this to M53's Print and
M62's labels.

## 7. When nobody is on a list

The command does not fail silently or produce an empty message. It says what to do: open People,
choose a brother, tick one of the two groups, and that it only has to be done once.

**"Now send it" needs a newsletter but deliberately not a PDF.** Somebody may want to warn the lodge
that this month's issue is coming, and refusing until they have exported would be the app deciding
the order of their evening.

## 8. What guards it

`tests/App.HeadlessTests/SendItTests.cs` (13): every address in BCC and nowhere else; a brother who
has passed is never emailed; no address means skipped rather than counted; a duplicate address is
one address; only the email group is emailed; too many addresses refuses the link; a lodge-sized
list still fits; the clipboard fallback comes in checkable batches; a short list is not chopped up
for no reason; the subject names the lodge and month, and still reads when the lodge name is blank;
the body says which file to attach; odd addresses are escaped so one cannot break the link.

### Failure-first evidence

Changing `&bcc=` to `&to=` failed `EveryAddressGoesInTheBlindCopyLineAndNowhereElse`; removing the
length cap failed `TooManyAddressesForOneLinkRefusesTheLinkRatherThanRiskingATruncatedOne`. Two
named failures, and only those.

## 9. What was NOT done

- **No "Open the folder" button.** PLAN.md mentions one; the message names the file and the print
  hand-off already opens it, and a third way to reach the same PDF is a third thing to explain.
  Recorded as a deliberate omission rather than an oversight — it is two lines if the owner wants it.
- **No send-and-forget.** The app never claims to have sent anything, because it never does.
- **No address validation.** A malformed address is the mail program's business to complain about,
  and refusing to open the message over one would strand the whole mailing.

Suite after M56: **1369 passing, 12 skipped** (13 new). No baseline moved.
