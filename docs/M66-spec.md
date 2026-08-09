# M66 — Bring in writing from a file

**Delivered 2026-08-09.** PLAN.md §11 M66.

## 1. What it is for

Committee members email their articles as Word documents. The workflow today is: open Word, select,
copy, switch to TrestleBoard, paste — a two-app round trip that carries formatting shrapnel across
with it. **Insert → Writing from a file…** reads `.docx` and `.txt` and puts the words on the page.

## 2. The line, drawn here and held

A Word document can carry tables, text boxes, footnotes, fields, tracked changes, embedded
spreadsheets and three kinds of numbering. **What comes in is paragraphs of words, mapped to this
app's own five paragraph styles, plus the pictures. Everything else is dropped.**

The plan named "fidelity temptation" as the recorded risk, and the reason it is a risk is that each
piece of fidelity looks small on its own. Tables next, then text boxes, then a column layout — and
the end of that road is a second layout engine that disagrees with the first about what a page is.
This app's WYSIWYG guarantee rests on there being exactly one.

What the user gets instead of fidelity is **honesty about what arrived**: the confirmation card
names the paragraph count, the word count and the picture count before anything happens, and says
plainly that anything in a table or a text box will not come through. Somebody who was sent a
three-page article and is told "2 paragraphs" has learned that the words were in a table — before
they printed sixty copies, rather than after.

Style mapping, `Word → ours`:

| Word | TrestleBoard |
| --- | --- |
| Title, Heading 1 | `heading` |
| Heading 2/3/4, Subtitle | `subheading` |
| Quote, Intense Quote, Block Text | `quote` |
| Caption | `caption` |
| anything else | `body` |

Title and Heading 1 land on the same style deliberately: in a four-page newsletter they are the
same thing, and splitting them would invent a level the stylesheet does not have.

Four things are dropped for reasons worth recording:

- **Tables, whole.** Their text arriving as a run of stray paragraphs would be *worse* than its
  absence — nobody would notice the columns had gone until it was printed. The app has its own
  tables, and they are the answer.
- **Field codes** (`w:instrText`) and **tracked deletions** (`w:delText`). Both look exactly like
  ordinary text to anything that only reads `w:t`, and both arrive as gibberish mid-sentence.
- **Character formatting.** Bold and italic runs come in as plain words. The paragraph style is the
  unit of meaning here; a bold run carried across is a decision made in another program's
  stylesheet.
- **Heading guessing in `.txt` files.** A short first line is not evidence. Being wrong about it
  often enough would be worse than leaving the committee to press the heading button.

A tab or a line break becomes a space, and runs of whitespace collapse — Word splits a sentence
across runs wherever the spell checker once stopped, and those joins arrive as stray gaps.

## 3. Pure managed, no Word, no COM

A `.docx` is a zip with XML in it, which the BCL already opens. `WritingImport` is a walk over
`word/document.xml` with an `XmlReader` and nothing else — no new package, and no Word to automate
on the Linux and macOS builds even if automating Word were a good idea.

A `.doc` (the older binary format) is refused with the instruction rather than attempted: open it in
Word, Save As, pick `.docx`.

## 4. A file from outside is not trusted

The file arrives by email from someone the committee knows, which is exactly the delivery route that
ends in trouble. So:

- **No DTD, no resolver.** An XML external entity referencing `file:///etc/passwd` is prohibited at
  the reader, and the document comes out as damaged — which is the right answer.
- **Sizes are capped**: 400,000 characters of text, 60 pictures, 40 MB per picture. A document that
  wants to add half a million characters to a six-page newsletter is a mistake or an attack, and
  either way refusing beats hanging the app laying it out.
- **Every failure is a sentence** (`UnsupportedFormatException`, the M25 standard). The one thing
  this code must never do is throw something raw at a committee member who opened the wrong file.

**Password-protected documents get their own message.** A protected `.docx` is a valid container
with one encrypted part, so it opens cleanly and simply has no `document.xml` — reported as
"damaged" it would send somebody hunting a fault that is not there. The reader looks for
`EncryptedPackage` and says: take the password off in Word, save, try again.

## 5. The pictures are offered, never taken

Images are read from `word/media/`, **bytes untouched**, and offered one at a time with a preview:
*"This picture came with the writing — use it?"* Accepting sends it through M18's single picture
ingest path, with the usual description and caption asks, so the original bytes land in the
container (gate 7) and the picture is a picture with nothing about its origin recorded anywhere.

Offered rather than inserted, because a Word document's media folder holds the author's letterhead
and their signature scan as readily as the photograph they meant to send. The card says so.

Only JPEG and PNG are offered — the two the app's own decoder reads. Showing a WMF and refusing it
a moment later is a worse experience than never showing it.

Pictures come out in entry-name order rather than in the order they appear in the writing. Following
the document's relationship graph would give the true order and costs three more XML parts that can
go wrong; since the user is shown each picture and asked, the order is convenience, not correctness.

## 6. One undo step

The acceptance says the import is a single undo step, and it is single **because the frame and all
its writing arrive as one composite command** — not because anything afterwards merges a run of
little edits back together. `FrameEditorController.AddTextFrameWith` is `AddTextFrame` with the
paragraphs supplied and a different label; both go through the same `CompositeCommand` of
`AddStoryCommand` + `AddBlockCommand`.

Each accepted picture is its own step afterwards. They are separate decisions, and undoing a
picture should not undo the article.

## 7. What guards it

`Core.Tests/WritingImportTests` — the reader, with no app. Every fixture is a `.docx` built in the
test out of fictional words; a hand-built zip is also the only way to write the damaged and
password-protected cases at all.

| Test | Holds |
| --- | --- |
| `TheWordsComeInAsParagraphs`, `WordsStylesBecomeOurs` (11 cases) | the mapping |
| `ASentenceSplitAcrossRunsComesBackWhole`, `ATabOrALineBreakBecomesASpaceRatherThanRunningWordsTogether`, `EmptyParagraphsAreDropped` | the tidying |
| `AListPointArrivesAsAListPoint` | M61's lists |
| `ATableIsLeftBehindEntirelyRatherThanArrivingAsLooseWords` | **the line** |
| `FieldCodesAndDeletedTextDoNotComeThrough` | the two invisible traps |
| `PicturesComeOutByteForByte` | gate 7 |
| `OnlyPicturesThisAppCanActuallyReadAreOffered` | no offer we cannot keep |
| `APasswordProtectedDocumentSaysSoAndSaysWhatToDo` | its own message |
| `AFileThatIsNotAZipAtAll…`, `AZipWithNoDocumentInIt…`, `TruncatedXmlIsADamagedDocumentAndNotARawXmlException` | sentences, never raw throws |
| `AnAbsurdlyLongDocumentIsRefusedRatherThanLaidOut` | the cap |
| `ADocumentCannotTalkTheReaderIntoFetchingSomething` | no external entities |
| `APlainTextFileComesInAsBodyParagraphs`, `ATextFileSavedByNotepadInUtf16StillReads` | `.txt`, including the BOM |

`App.HeadlessTests/BringInWritingTests` — the newsletter: one frame and one undo; Word's styles
become ours; cancelling changes nothing; a picture is offered and can be left out; a file that is
not a document refuses in a sentence and touches nothing; `.txt` works; the command needs a
newsletter and says so.

`docs/accessibility-test-script.md` gains §19.

**A test seam was added along the way:** `MainWindow.SwallowErrorsForTest` records the error in
`LastErrorForTest` and skips the modal window. A headless test has nobody to press an error
dialog's button, so before this the app's failure paths were the ones the shell tests could not
reach — which is exactly backwards.

## 8. What was NOT done

- **No tables, text boxes, footnotes or columns.** §2, and the spec holds the line rather than
  defending it later.
- **No bold or italic.** The paragraph style is the unit that comes across.
- **No `.doc`, `.rtf`, `.odt` or `.pages`.** `.doc` refuses with the instruction; the rest are not
  what this committee is sent.
- **No numbered-list distinction.** A `w:numPr` says a paragraph is a list point; whether it is
  bulleted or numbered lives in `numbering.xml`, several indirections away. Bulleted is the honest
  guess — a bullet where a number belonged is a smaller wrong than a number starting again at one
  in the middle of somebody's list, and M61's command changes it in one press.
- **No import into an existing frame.** The writing arrives in a new frame, which the user drags
  where they want it. Flowing it into a chosen frame would raise "what happens to what is already
  in there", and the answer that is not destructive is a new frame.
