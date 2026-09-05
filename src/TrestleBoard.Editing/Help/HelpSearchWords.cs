using System;
using System.Collections.Generic;
using TrestleBoard.Editing.Actions;

namespace TrestleBoard.Editing.Help;

/// <summary>
/// What somebody might type instead of the word the app uses (PLAN.md §11 M63).
///
/// <para>The help index is generated from the catalog, so every command is findable by its own
/// name without anything being written here. This table exists for the gap between the app's
/// vocabulary and the user's: the app says "photo" and a seventy-year-old types "picture"; the app
/// says "Make the PDF" and they type "print"; the app says "address book" and they type "members".
/// A search box that answers "no results" to a word the user actually knows is worse than no search
/// box, because it teaches them the answer is not in there.</para>
///
/// <para><b>Deliberately partial.</b> Most commands are not listed, because most titles already
/// contain the word anybody would look for — "Bold" is searched for as "bold". Listing every one of
/// the catalog's hundred-odd entries would be a hundred-odd chances to write a synonym nobody uses,
/// and the corpus test
/// in <c>HelpIndexTests</c> is what actually proves the search works, not the size of this table.
/// </para>
///
/// <para>Every id here must be a real action: <c>EverySynonymNamesARealAction</c> holds it, the
/// same guard <c>KeyboardAuditTests.EveryRegisteredGestureNamesARealAction</c> puts on the keyboard
/// table. A renamed action leaves dead search words behind otherwise, and dead search words fail
/// silently — the user simply does not find the thing.</para>
/// </summary>
public static class HelpSearchWords
{
    private static readonly Dictionary<string, string[]> Words = new(StringComparer.Ordinal)
    {
        // The word this audience uses for a photograph is "picture", and the app says "photo".
        [ActionId.InsertPhoto] = ["picture", "image", "photograph", "add a picture"],

        // M79. Nobody types "frame style". They type the shape they can see in their head.
        [ActionId.AddRule] = ["line", "rule", "divider", "underline the heading", "separator", "bar"],

        // M81. "Duplicate" is a word from another program; what people say is "another one".
        [ActionId.Duplicate] = ["copy", "duplicate", "another one", "same again", "second one"],
        // M93. Nobody types "leading" or "paragraph spacing". They describe the page.
        // M96. "Align" is the trade word; "centre" is the one that gets typed, spelled both ways.
        [ActionId.AlignTextCentre] =
            ["centre", "center", "middle", "align", "centred heading", "in the middle"],
        [ActionId.AlignTextLeft] = ["align", "back to normal", "left edge"],
        [ActionId.AlignTextRight] = ["align", "right edge", "against the right"],

        // M100. What somebody says is "the same again", never "duplicate page".
        [ActionId.DuplicatePage] =
            ["copy the page", "same page again", "another page like this", "repeat the layout",
             "same layout"],

        // M97. Nobody types "page setup" until they have failed to find it another way.
        [ActionId.PageSetup] =
            ["paper", "margins", "A4", "letter", "legal", "page size", "edges", "white space round "
             + "the edge", "landscape", "wider"],

        // M99. Again not the bare word "colours", which the appearance settings have owned since M16.
        [ActionId.TextColour] =
            ["colour the writing", "red text", "coloured heading", "ink", "make it blue"],

        // M98. Somebody describes the state of the words, not the name of the command.
        [ActionId.ChangeCase] =
            ["capitals", "caps", "shouting", "all in capitals", "lower case", "upper case",
             "title case", "wrong case"],
        [ActionId.WordCount] = ["how long", "how many words", "count", "length", "word count"],
        [ActionId.InsertSymbol] =
            ["dash", "em dash", "degree", "fraction", "half", "copyright", "bullet point",
             "special character", "symbol", "not on the keyboard"],

        [ActionId.WritingLook] =
            ["spacing", "line spacing", "cramped", "squashed", "spread out", "double space",
             "more air", "indent", "tab the first line", "too tight"],

        // M94. Somebody hunting for this is describing a measurement, not a feature.
        [ActionId.PositionAndSize] =
            ["exact", "inches", "measure", "line it up by numbers", "same place as last time",
             "type the size", "nudge it precisely"],

        // M95. "Rectangle" and "shape" are what other programs call it; this audience describes
        // the job it does on the page.
        [ActionId.AddBox] =
            ["box", "panel", "rectangle", "shape", "block of colour", "highlight a notice",
             "coloured background"],
        // "colour" alone belongs to the app's own appearance settings and has since M16 — this is
        // about one box on the page, so it says so.
        [ActionId.ShapeColours] =
            ["recolour the box", "fill", "outline", "change the box", "shade the panel"],

        [ActionId.ToggleLocked] = ["lock", "keep it still", "stop it moving", "pin", "fix in place"],

        // M91. Nobody searches for "move to page". They describe the mistake they have made.
        [ActionId.MoveToNextPage] =
            ["wrong page", "move it over", "put it on page 4", "shift it along", "belongs on another page"],
        [ActionId.MoveToPreviousPage] = ["wrong page", "move it back", "page before", "one page back"],

        // M91. Paste stopped being only about words, and "paste" is not what this audience says.
        [ActionId.Paste] = ["put it down", "put it here", "the one I copied"],
        [ActionId.Copy] = ["take a copy", "same one again"],
        [ActionId.Cut] = ["take it off the page", "move it somewhere else"],

        // M82. What somebody types is the symptom, not the cure.
        [ActionId.GrowToFit] =
            ["too long", "does not fit", "runs over", "bigger box", "taller", "more room", "cut off"],

        // M83. "Columns" is in the title; these are what somebody says instead.
        [ActionId.ToggleTwoColumns] = ["two columns", "newspaper", "side by side", "split the text"],

        // M84. "Calendar" alone would compete with the district one, which has had the word since
        // M7; every word here is one somebody uses for the month-on-a-grid view specifically.
        [ActionId.InsertMonthCalendar] =
            ["month at a glance", "grid", "dates", "what is on", "diary", "wall calendar"],
        // Deliberately NOT the bare word "picture": the help index ranks by synonym, and this
        // entry outranked "Insert a picture" for somebody typing the one word that means putting a
        // photograph on the page. Every word here is one that only means SHARING.
        [ActionId.ExportPagePicture] =
            ["facebook", "share", "jpeg", "screenshot", "post it", "text message", "social media"],
        [ActionId.ToggleBorder] = ["border", "box", "outline", "frame", "line round it", "boxed notice"],
        [ActionId.ToggleShade] = ["shade", "shading", "background", "grey box", "highlight", "tint"],

        // M78. "Footer" is a word from another program. What people ask for is the number.
        [ActionId.ShowPageFooter] =
            ["page number", "numbering", "footer", "bottom of the page", "which page", "page 3 of 6"],

        // M77. Nobody looks for "problem report" — they look for the word for what happened, and
        // every one of these is a word this audience has used down a telephone.
        [ActionId.SaveProblemReport] =
            ["crash", "broken", "error", "bug", "went wrong", "not working", "froze", "help me"],
        [ActionId.ReplacePicture] = ["picture", "image", "change the picture", "swap"],
        [ActionId.FixPhoto] = ["picture", "image", "sideways", "rotate", "upside down"],
        [ActionId.AdjustPhoto] = ["picture", "image", "brightness", "dark", "washed out"],
        [ActionId.PositionPicture] = ["picture", "image", "crop", "move the picture"],
        [ActionId.CaptionPicture] = ["picture", "image", "words under the picture"],
        [ActionId.DescribePicture] = ["picture", "image", "blind", "screen reader", "alt text"],

        // "Print" is what people call making the PDF, because the PDF is what they print.
        [ActionId.ExportPdf] = ["print", "pdf", "finish", "send to the printer", "make it"],
        [ActionId.PrintPdf] = ["pdf", "paper", "printer"],
        [ActionId.ExportDraftPdf] = ["print", "pdf", "proof", "draft copy", "watermark"],
        [ActionId.SendIt] = ["email", "e-mail", "mail", "send out", "distribute", "post"],

        // The monthly cycle, in the words the committee uses for it.
        [ActionId.StartFromLastMonth] = ["new", "next month", "begin", "start", "carry forward"],
        [ActionId.NewFromTemplate] = ["new", "blank", "start", "layout"],

        // M75. Somebody hunting for this has typed a month name into the search box, or is looking
        // for the word "date" — which is also what they call the meeting date on the cover, so both
        // spellings of the confusion lead here.
        [ActionId.SetIssueDate] = ["date", "month", "year", "issue", "wrong month", "change the date"],
        [ActionId.RestoreDocument] = ["lost", "crash", "gone", "recover", "backup", "power cut"],
        [ActionId.ReviewNewsletter] = ["check", "mistakes", "before i send", "look it over"],
        [ActionId.CheckSpelling] = ["spelling", "spell check", "misspelled", "typo"],
        [ActionId.ReadAloud] = ["speak", "voice", "hear", "proofread", "out loud"],
        [ActionId.ShowLastYear] = ["last year", "previous", "old issue", "what did we say"],

        // The address book. Nobody calls it that until the app teaches them to.
        [ActionId.ShowPeople] = ["members", "roster", "brethren", "addresses", "people", "list"],
        [ActionId.ImportPeople] = ["members", "roster", "spreadsheet", "excel", "csv", "bring in"],
        [ActionId.ExportPeople] = ["members", "roster", "spreadsheet", "excel", "save the list"],

        // The words on the page.
        [ActionId.BulletList] = ["bullet", "points", "dots", "list"],
        [ActionId.NumberList] = ["numbered", "numbers", "1 2 3", "list"],
        [ActionId.FontsAndStyles] = ["font", "typeface", "size", "look", "appearance"],
        // The two complaints are mirror images, so their words must not overlap: somebody typing
        // "too small" wants the writing made bigger, and a shared word would let Smaller win it.
        [ActionId.BiggerText] = ["font", "size", "larger", "too small", "cannot read"],
        [ActionId.SmallerText] = ["font", "size", "shrink", "does not fit", "runs over"],
        [ActionId.InsertPhrase] = ["death", "passed away", "sick", "funeral", "hard news", "wording"],

        // Undoing a mistake is the single most-asked question from this audience.
        [ActionId.Undo] = ["mistake", "wrong", "go back", "revert", "oops"],
        [ActionId.Redo] = ["mistake", "forward", "put it back"],

        // Bringing writing in (M66). Nobody types "import" — they name the program it came from.
        [ActionId.BringInWriting] =
            ["word", "docx", "document", "article", "import", "paste", "text file", "emailed me",
             "someone sent"],

        // A page from a PDF (M67). They name the thing they were sent, not the file format.
        [ActionId.BringInPdfPage] =
            ["pdf", "flyer", "notice", "grand lodge", "district", "calendar page", "page from",
             "someone sent", "scan"],

        // The emblem shelf (M65). Nobody searches for "emblem" either — they search for the thing.
        [ActionId.InsertEmblem] =
            ["emblem", "square and compasses", "logo", "symbol", "picture", "ornament", "star",
             "clip art", "decoration"],

        // Handing over (M64). Nobody searches for "pack" — they search for the event.
        [ActionId.PackUpForSuccessor] =
            ["successor", "handover", "hand over", "new computer", "leaving", "move everything", "backup"],
        [ActionId.BringInAPack] =
            ["successor", "handover", "predecessor", "new computer", "took over", "restore everything"],

        // Looking at the app itself.
        [ActionId.Settings] = ["colours", "colors", "dark", "theme", "bigger app", "contrast"],
        [ActionId.ZoomIn] = ["bigger", "closer", "magnify", "cannot see"],
        [ActionId.ZoomOut] = ["smaller", "further", "whole page"],
        [ActionId.ShowExampleIssue] = ["example", "sample", "what should it look like"],
    };

    /// <summary>The words for one action, or nothing if the title already says it.</summary>
    public static IReadOnlyList<string> For(string actionId) =>
        Words.TryGetValue(actionId, out string[]? words) ? words : [];

    /// <summary>Every action that has extra words, for the test that they all still exist.</summary>
    public static IReadOnlyCollection<string> ActionsWithExtraWords => Words.Keys;
}
