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
        new(Key.X, Ctrl, ActionId.Cut, KeyScope.WhileTyping),
        new(Key.C, Ctrl, ActionId.Copy, KeyScope.WhileTyping),
        new(Key.V, Ctrl, ActionId.Paste, KeyScope.WhileTyping),
        new(Key.A, Ctrl, ActionId.SelectAll, KeyScope.WhileTyping),

        // M21. Both are KeyScope.Always: Ctrl+F is most useful while the caret is already in a
        // frame, and the two gestures every other publishing program uses are the two gestures a
        // user will try.
        new(Key.F, Ctrl, ActionId.Find),
        new(Key.H, Ctrl, ActionId.Replace),

        // ---- Text ---------------------------------------------------------------------------
        new(Key.B, Ctrl, ActionId.Bold, KeyScope.WhileTyping),
        new(Key.I, Ctrl, ActionId.Italic, KeyScope.WhileTyping),

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

        // ---- The address book (M12) ---------------------------------------------------------------
        new(Key.R, CtrlShift, ActionId.ShowPeople),

        // ---- Looking at it ----------------------------------------------------------------------
        new(Key.OemPlus, Ctrl, ActionId.ZoomIn),
        new(Key.OemMinus, Ctrl, ActionId.ZoomOut),
        new(Key.D0, Ctrl, ActionId.ActualSize),
        new(Key.D1, Ctrl, ActionId.FitPage),
        new(Key.F6, KeyModifiers.None, ActionId.NextRegion),
        new(Key.F6, KeyModifiers.Shift, ActionId.PreviousRegion),
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
            _ => shortcut.Key.ToString(),
        };
    }
}
