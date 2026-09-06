using Avalonia.Input;
using TrestleBoard.Editing.Actions;

namespace TrestleBoard.App.Actions;

/// <summary>When a gesture applies, relative to whether the caret is in a story.</summary>
internal enum KeyScope
{
    /// <summary>Always, whether or not the user is typing.</summary>
    Always,

    /// <summary>Only while the caret is in a story — Ctrl+C must not cut a frame.</summary>
    WhileTyping,

    /// <summary>Only when the caret is NOT in a story — bare PageDown belongs to the caret.</summary>
    WhileNotTyping,
}

/// <summary>One key press and the action it runs.</summary>
internal sealed record KeyShortcut(Key Key, KeyModifiers Modifiers, string ActionId, KeyScope Scope = KeyScope.Always);

/// <summary>
/// Which key press runs which action (PLAN.md §11 M11), replacing the 126-line
/// <c>case Key.X when …</c> switch this window used to carry.
///
/// The switch had a failure mode that only a user could notice: <c>case Key.Y when ctrl:</c> also
/// matches Ctrl+Shift+Y, so it silently swallowed the shifted gesture the menu advertised. A table
/// matched on <em>exact</em> modifiers cannot do that — Ctrl+Shift+Y is simply a different row from
/// Ctrl+Y — which is why the audit test that used to read the source for that pattern could be
/// replaced by one that presses every registered key and checks where it lands.
/// </summary>
internal static class KeyboardMap
{
    private const KeyModifiers Ctrl = KeyModifiers.Control;
    private const KeyModifiers CtrlShift = KeyModifiers.Control | KeyModifiers.Shift;

    private static readonly KeyShortcut[] Bindings =
    [
        // ---- The newsletter -----------------------------------------------------------------
        new(Key.O, Ctrl, ActionId.Open),

        // M24. Ctrl+S is the one gesture every user of this app already has in their fingers from
        // Word, and until now it did nothing at all — there was no command behind it. KeyScope.Always
        // on purpose: reaching for Ctrl+S in the middle of typing a paragraph is exactly when it is
        // wanted, and the text session is untouched by a save.
        new(Key.S, Ctrl, ActionId.Save),
        new(Key.S, CtrlShift, ActionId.SaveAs),

        new(Key.E, Ctrl, ActionId.ExportPdf),

        // ---- Edit ---------------------------------------------------------------------------
        new(Key.Z, Ctrl, ActionId.Undo),
        new(Key.Y, Ctrl, ActionId.Redo),
        // M91: NOT WhileTyping any more, for exactly the reason the note below gives about paste.
        // Cut and copy now mean two things — the highlighted words inside a story, the chosen thing
        // outside one — and a row scoped to typing would have left the second half unreachable from
        // the keyboard on the day it shipped. `NoGestureIsScopedNarrowerThanTheCommandItRuns` is the
        // test that ties this row to the catalog's answer.
        new(Key.X, Ctrl, ActionId.Cut),
        new(Key.C, Ctrl, ActionId.Copy),
        // M28: NOT WhileTyping. M18 made paste mean two things — words into a story, or a picture
        // from the clipboard onto the page — and the catalog says so (`IsEditingText || HasDocument`),
        // and the Edit menu advertises Ctrl+V. Only this row still believed paste was about typing,
        // so the keyboard half of M18's feature was unreachable from the day it shipped: a user who
        // copied a photo, clicked a frame and pressed Ctrl+V got nothing at all, silently
        // (review §14.2). Cut and copy joined it at M91, when they stopped needing a caret too.
        new(Key.V, Ctrl, ActionId.Paste),
        new(Key.A, Ctrl, ActionId.SelectAll, KeyScope.WhileTyping),

        // M50. Tab already walks the page's blocks one at a time; holding Ctrl keeps what is
        // already chosen instead of replacing it, which is the same relationship Shift+click has
        // to a plain click. Scoped away from typing, where Tab leaves the writing (M44).
        new(Key.Tab, Ctrl, ActionId.AddNextToSelection, KeyScope.WhileNotTyping),
        new(Key.Tab, CtrlShift, ActionId.AddPreviousToSelection, KeyScope.WhileNotTyping),


        // M21. Both are KeyScope.Always: Ctrl+F is most useful while the caret is already in a
        // frame, and the two gestures every other publishing program uses are the two gestures a
        // user will try.
        new(Key.F, Ctrl, ActionId.Find),
        new(Key.H, Ctrl, ActionId.Replace),

        // ---- Text ---------------------------------------------------------------------------
        new(Key.B, Ctrl, ActionId.Bold, KeyScope.WhileTyping),
        new(Key.I, Ctrl, ActionId.Italic, KeyScope.WhileTyping),

        // M102. Ctrl+U is the gesture every editor uses for underline and it was free — M99's
        // "put the writing back" is Ctrl+Shift+U, which is birthdays, and neither collides.
        new(Key.U, Ctrl, ActionId.Underline, KeyScope.WhileTyping),

        // M103. Shift+Enter is the gesture every editor uses for a line that is not a new
        // paragraph. WhileTyping, because a bare Enter outside a story means something else and
        // this must never reach the canvas's own Enter handling.
        new(Key.Enter, KeyModifiers.Shift, ActionId.LineBreak, KeyScope.WhileTyping),

        // ---- Fonts and sizes (M14) ------------------------------------------------------------
        // Ctrl+Shift+T would have been the mnemonic choice, but M11 already gave it to "add a text
        // frame" and a promise the app cannot keep is worse than an unmemorable one.
        new(Key.D, CtrlShift, ActionId.FontsAndStyles),
        new(Key.OemPeriod, CtrlShift, ActionId.BiggerText, KeyScope.WhileTyping),
        new(Key.OemComma, CtrlShift, ActionId.SmallerText, KeyScope.WhileTyping),

        // ---- Putting things on the page -------------------------------------------------------
        new(Key.T, CtrlShift, ActionId.AddTextFrame),
        new(Key.P, CtrlShift, ActionId.InsertPhoto),

        // ---- The selected thing -----------------------------------------------------------------
        // Delete belongs to the caret while typing; outside a story it removes the chosen frame.
        new(Key.Delete, KeyModifiers.None, ActionId.DeleteFrame, KeyScope.WhileNotTyping),

        // M28: Backspace does the same thing, and used to do it from a `case` in the canvas that
        // this table knew nothing about — so the audit could not see it, the panel could not
        // advertise it, and it could not be refused with a reason like every other command. Both
        // keys reach a frame the same way now.
        new(Key.Back, KeyModifiers.None, ActionId.DeleteFrame, KeyScope.WhileNotTyping),
        // M81. Ctrl+D is the gesture every publishing program uses for "make another like this",
        // and it was free. Scoped away from typing, where D belongs to the caret.
        new(Key.D, Ctrl, ActionId.Duplicate, KeyScope.WhileNotTyping),

        // M91. Ctrl+Page Up/Down already turn the page; adding Shift reads as "and take this with
        // me", which is what the command does. WhileNotTyping because Page Up and Page Down belong
        // to the caret inside a story.
        new(Key.PageDown, CtrlShift, ActionId.MoveToNextPage, KeyScope.WhileNotTyping),
        new(Key.PageUp, CtrlShift, ActionId.MoveToPreviousPage, KeyScope.WhileNotTyping),
        // M96. Ctrl+L / Ctrl+E / Ctrl+R are what every editor uses for lining writing up, and all
        // three were free. WhileTyping, because that is when the catalog offers them.
        new(Key.L, Ctrl, ActionId.AlignTextLeft, KeyScope.WhileTyping),
        // NOT Ctrl+E, which every other editor uses for centring and which this app has spent on
        // "Make the PDF…" since M8 — an export is the more important command and it was there
        // first. Ctrl+Shift+C for centre instead: C is the letter of the word.
        new(Key.C, CtrlShift, ActionId.AlignTextCentre, KeyScope.WhileTyping),
        new(Key.R, Ctrl, ActionId.AlignTextRight, KeyScope.WhileTyping),

        new(Key.E, CtrlShift, ActionId.EditWidget),
        new(Key.G, CtrlShift, ActionId.EditWidgetList),
        new(Key.Y, CtrlShift, ActionId.FitToContents),
        new(Key.U, CtrlShift, ActionId.SyncBirthdays),

        // M19. Every letter in "officers" and "fill in" was already spoken for — O is the picture
        // replace, F is fix-this-picture — so the officers sync takes Ctrl+Shift+B and the menu item
        // carries the words. An unmemorable chord is a smaller sin than a stolen one.
        new(Key.B, CtrlShift, ActionId.SyncOfficers),

        // ---- Pictures ---------------------------------------------------------------------------
        new(Key.F, CtrlShift, ActionId.FixPhoto),
        new(Key.A, CtrlShift, ActionId.AdjustPhoto),

        // M18. Ctrl+Shift+O reads as "open a picture into this frame", beside Ctrl+O for opening a
        // newsletter. The other three picture commands are menu-only on purpose: they are typed
        // into once and then left alone, and three more unmemorable chords would buy nothing.
        new(Key.O, CtrlShift, ActionId.ReplacePicture),

        // ---- How text flows ---------------------------------------------------------------------
        new(Key.W, CtrlShift, ActionId.ToggleWrap),
        new(Key.L, CtrlShift, ActionId.LinkFrames),
        new(Key.K, CtrlShift, ActionId.UnlinkFrames),
        new(Key.M, CtrlShift, ActionId.AutoFlow),

        // ---- Arranging --------------------------------------------------------------------------
        new(Key.OemCloseBrackets, Ctrl, ActionId.BringForward),
        new(Key.OemOpenBrackets, Ctrl, ActionId.SendBackward),
        new(Key.OemCloseBrackets, CtrlShift, ActionId.BringToFront),
        new(Key.OemOpenBrackets, CtrlShift, ActionId.SendToBack),

        // ---- Pages ------------------------------------------------------------------------------
        new(Key.PageDown, Ctrl, ActionId.NextPage),
        new(Key.PageUp, Ctrl, ActionId.PreviousPage),
        // Bare PageUp/PageDown moves the caret while typing, and turns the page otherwise.
        new(Key.PageDown, KeyModifiers.None, ActionId.NextPage, KeyScope.WhileNotTyping),
        new(Key.PageUp, KeyModifiers.None, ActionId.PreviousPage, KeyScope.WhileNotTyping),

        // M28: two commands that had no gesture at all. Adding a page is one of the handful of
        // things somebody does every month while building an issue, and the settings window is
        // where the UI is made bigger — the one window an elderly user most needs to find. The
        // other four page commands stay menu-only: they are rare, and four more chords nobody can
        // remember would buy nothing (the same trade M21 made for align and distribute).
        new(Key.N, CtrlShift, ActionId.AddPage),
        new(Key.F10, KeyModifiers.None, ActionId.Settings),

        // ---- The address book (M12) ---------------------------------------------------------------
        new(Key.R, CtrlShift, ActionId.ShowPeople),

        // ---- Looking at it ----------------------------------------------------------------------
        new(Key.OemPlus, Ctrl, ActionId.ZoomIn),
        new(Key.OemMinus, Ctrl, ActionId.ZoomOut),
        new(Key.D0, Ctrl, ActionId.ActualSize),
        new(Key.D1, Ctrl, ActionId.FitPage),

        // M108. Next to Ctrl+1 because it is the same question asked a second way, and Ctrl+2 was
        // free — the digits above 1 claim nothing in this application.
        new(Key.D2, Ctrl, ActionId.FitWidth),
        new(Key.F6, KeyModifiers.None, ActionId.NextRegion),
        new(Key.F6, KeyModifiers.Shift, ActionId.PreviousRegion),

        // ---- Asking the app (M63) ----------------------------------------------------------------
        // F1 is the one shortcut this audience already knows from every other program they have
        // ever used, and it is the only unmodified function key here that is not already taken.
        new(Key.F1, KeyModifiers.None, ActionId.HowDoI),
    ];

    internal static IReadOnlyList<KeyShortcut> All => Bindings;

    /// <summary>
    /// Which action this key press runs, or null. Modifiers are compared for EQUALITY, not with
    /// HasFlag — that one choice is what makes a shifted gesture unreachable-by-construction rather
    /// than unreachable-by-accident.
    /// </summary>
    internal static string? Resolve(Key key, KeyModifiers modifiers, bool isTyping)
    {
        foreach (KeyShortcut shortcut in Bindings)
        {
            if (shortcut.Key != key || shortcut.Modifiers != modifiers)
            {
                continue;
            }

            bool applies = shortcut.Scope switch
            {
                KeyScope.WhileTyping => isTyping,
                KeyScope.WhileNotTyping => !isTyping,
                _ => true,
            };
            if (applies)
            {
                return shortcut.ActionId;
            }
        }

        return null;
    }

    /// <summary>
    /// The gesture as the user would read it — "Ctrl+Shift+F". This is what the panel prints beside
    /// an action, and what the audit test compares against the catalog's advertised shortcut, so a
    /// promise the app cannot keep fails the build instead of quietly disappointing someone.
    /// </summary>
    internal static string Describe(KeyShortcut shortcut)
    {
        string text = shortcut.Modifiers.HasFlag(KeyModifiers.Control) ? "Ctrl+" : string.Empty;
        if (shortcut.Modifiers.HasFlag(KeyModifiers.Shift))
        {
            text += "Shift+";
        }

        if (shortcut.Modifiers.HasFlag(KeyModifiers.Alt))
        {
            text += "Alt+";
        }

        return text + shortcut.Key switch
        {
            Key.OemPlus => "=",
            Key.OemMinus => "-",
            Key.OemOpenBrackets => "[",
            Key.OemCloseBrackets => "]",
            Key.OemPeriod => ".",
            Key.OemComma => ",",
            Key.D0 => "0",
            Key.D1 => "1",
            Key.D2 => "2",

            // M103. Avalonia's Key.Enter IS Key.Return, and the enum reports the older name — so
            // this printed "Shift+Return" for a key every keyboard in the lodge calls Enter.
            // Caught by EveryShortcutTheCatalogAdvertisesIsInTheTable, which is the test that
            // exists to stop the app promising a gesture in words nobody would recognise.
            Key.Return => "Enter",
            _ => shortcut.Key.ToString(),
        };
    }
}
