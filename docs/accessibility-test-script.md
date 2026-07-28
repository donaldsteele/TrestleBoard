# TrestleBoard — Manual Screen-Reader Test Script

**Status: WRITTEN, NOT YET EXECUTED.** This document is the M9 deliverable required by
`docs/M9-spec.md` §7 and PLAN.md §11-M9. Writing it is something an agent can do by reading the
source; *running* it is not — it requires a person sitting at a real machine with NVDA or
VoiceOver actually turned on, listening to what the screen reader actually says, and writing it
down. No automated test substitutes for that, and none has been run in place of it. Nothing in
this document should be read as "this passed" or "this was tested" — it is the instrument, not
the result. `docs/M9-spec.md` §8 tracks the executed pass as an open item.

**Who this is for.** You do not need to be a programmer. You need about 30 minutes, a computer
with NVDA (Windows) or a Mac with VoiceOver, and a willingness to write down exactly what you
hear — even when what you hear is "nothing."

**Before you begin, please read "Problems found while writing this script" at the end of this
document.** It lists several places where the app does not yet do what it is supposed to. Those
sections are marked clearly below too, but it saves confusion to know up front that a few of the
ten sections describe features (a start screen, templates, crash recovery) that do not exist in
the app yet, and one keyboard shortcut is confirmed broken. You are not doing anything wrong if
those steps don't work — that is what this script is for.

---

## Setup

### Getting the app running

TrestleBoard is not packaged yet (that is milestone M10), so there is no installer. From a
checkout of the repository, with the .NET 10 SDK installed:

```
dotnet run --project src/TrestleBoard.App
```

This opens the main window directly — there is no splash or start screen (see §1 below).

### Windows — NVDA

1. Download and install NVDA from nvaccess.org (free).
2. Start NVDA. It announces itself when it starts.
3. Turn on the **Speech Viewer** so you can copy-paste exactly what NVDA said instead of
   re-typing it from memory: NVDA menu (`NVDA+N`) ▸ Tools ▸ Speech Viewer. A small window opens
   and fills with every phrase NVDA speaks. Keep it visible (or Alt+Tab to it) as you work through
   the script, and copy each relevant line into the "Heard" column of the results table.
4. Basic NVDA keys you'll use: `Tab`/`Shift+Tab` to move focus, `NVDA+Down Arrow` to read the
   current line/item again if you missed it, `Insert` is the default NVDA key if `NVDA+…`
   shortcuts don't respond (some laptops use `Insert` instead of the Caps Lock/NVDA key).

### macOS — VoiceOver

1. Turn VoiceOver on/off with `Cmd+F5` (or ask Siri "turn on VoiceOver").
2. Turn on the **Caption Panel** so you can read (and copy) what VoiceOver says instead of relying
   on memory: open VoiceOver Utility (`VO+F8` — VO is `Control+Option` by default), go to the
   General category, and enable "Show caption panel." (The exact menu wording has moved around
   between macOS versions; if you can't find it, search System Settings for "VoiceOver Utility.")
3. Basic VoiceOver keys: `VO+Right/Left Arrow` to move between items, `Tab` to move between
   controls in a window, `VO+Space` to activate the current item.

### Linux — Orca (best-effort)

PLAN.md §6 is explicit that Linux screen-reader support (AT-SPI, read by Orca) is
**best-effort — the weakest of the three Avalonia targets, not a tested/guaranteed level.** If you
are testing on Linux: install and start Orca (`orca` from your distribution's screen-reader
package, or `Super+Alt+S` on GNOME), and work through the same steps. For every Linux step,
**write down what you observe rather than marking Pass/Fail** — there is no promised behaviour to
grade against here, only a record of the current state. Mark these rows "Observed" in the results
table, not Pass or Fail.

### A note on what "PASS" means below

Every step tells you exactly what you should **hear**. Write down what you actually heard —
word for word if you can (copy it out of the Speech Viewer / Caption Panel). "The button seemed
accessible" is not a usable answer; "NVDA said 'Fix photo, button'" is.

---

## 1. Launch and the start screen

> **This section tests what actually happens at launch, not the start screen described in
> PLAN.md §7.** No start screen exists in this build — see Finding 4 at the end of this document.
> The app opens straight into the main editing window with no document loaded.

**1.1** Launch the app (`dotnet run --project src/TrestleBoard.App`).
Expected: your screen reader announces a window. Per the source
(`src/TrestleBoard.App/MainWindow.axaml`, `AutomationProperties.Name="TrestleBoard main window"`),
it should say something containing **"TrestleBoard main window."** Write down exactly what you
heard.

**1.2** Press `Tab` a few times from first launch.
Expected: focus moves into the menu bar (File is the first menu) and then to the "Open" toolbar
button. Most other toolbar buttons and menu items are disabled because no document is open yet —
NVDA/VoiceOver should say "unavailable" or "dimmed" for those. Note which ones you land on and
whether each is announced as unavailable.

**1.3** Open the File menu (`Alt+F` or arrow onto it and press `Enter`/`Down Arrow`).
Expected: you hear "File menu," then each item: "Open a newsletter," "Open the sample
newsletter," "Export as PDF" (this one should say unavailable/dimmed — no document is open),
"Exit TrestleBoard."

---

## 2. Opening a template

> **This section tests the closest thing that currently exists to "open a template."** The three
> embedded templates described in PLAN.md §7 and `docs/M9-spec.md` §2 (Classic 414, Simple
> 4-page, 6-page with photos) do not exist in the source yet — see Finding 4. What exists is a
> single built-in sample document and ordinary file-open.

**2.1** From the File menu, choose "Open the sample newsletter" (`OnOpenSampleClicked` — no
keyboard shortcut is advertised for this one, only the menu path).
Expected: the document loads; the window title changes (screen readers do not always announce a
title change automatically — check whether yours does). The page/zoom labels in the toolbar
should now show real values instead of "No newsletter."

**2.2** Tab to the **"Current page"** label in the toolbar.
Expected to hear: something like **"Page 1 of *n*, Current page."** Write down the exact number
of pages announced.

**2.3** Tab to the **"Current zoom"** label.
Expected to hear: something like **"100%, Current zoom."**

**2.4** From the File menu, choose "Open a newsletter…" (`Ctrl+O`).
Expected: an operating-system file-picker dialog opens (this is not TrestleBoard's own UI — its
accessibility is Windows'/macOS's responsibility, not the app's). Confirm your screen reader can
navigate it normally, then press Escape or Cancel to close it without opening a file.

---

## 3. The main window: menus, toolbar, page/zoom labels, status line

Work through every menu below. For each item, press `Tab`/arrow onto it (or open the menu with
`Alt+`the underlined letter) and write down exactly what is announced. All names below come
directly from `AutomationProperties.Name` in `src/TrestleBoard.App/MainWindow.axaml` — if you hear
something different, that is itself a finding, write it down.

> **The menu bar was restructured in M17** (PLAN.md §11 M17, `docs/M17-spec.md`). If you are
> holding a printed copy of this script older than that, throw it away: there is no longer an
> "Object" menu or a "This item" menu, and the toolbar has nine controls rather than eighteen.
> **Nine top-level menus, and every command in the app has a menu item** — that second promise is
> now enforced by `MenuIndexTests.EveryActionInTheCatalogHasAMenuItem`, so if you find a command
> with no menu home, that is a test that has been switched off, not a menu that was forgotten.

**3.1 File menu** (`Alt+F`): Open a newsletter (`Ctrl+O`) · Open the sample newsletter · Start from
a template · Start this month from last month's newsletter · Export as PDF (`Ctrl+E`) · Exit
TrestleBoard.

**3.2 Edit menu** (`Alt+E`): Undo (`Ctrl+Z`) · Redo (`Ctrl+Y`) · Cut (`Ctrl+X`) · Copy (`Ctrl+C`) ·
Paste (`Ctrl+V`) · Select all (`Ctrl+A`) · **Find (`Ctrl+F`)** · **Find and replace (`Ctrl+H`)** ·
Delete this (`Delete`) · Change what this item says
(`Ctrl+Shift+E`) · Edit this list (`Ctrl+Shift+G`). With no document open, or nothing selected,
most of these should announce as unavailable — check. **Undo and Redo say what they will take
back** ("Undo move photo"), so their spoken name changes as you work; that is deliberate.

**3.3 Format menu** (`Alt+O`): Bold (`Ctrl+B`) · Italic (`Ctrl+I`) · Paragraph style (a submenu,
built from the newsletter's own styles) · Fonts and text styles (`Ctrl+Shift+D`) · Make text bigger
(`Ctrl+Shift+.`) · Make text smaller (`Ctrl+Shift+,`) · Use a different font just here · Put it back
to the usual font · **Picture** (a submenu: Fix this picture `Ctrl+Shift+F`, Adjust the picture
`Ctrl+Shift+A`). Bold and Italic should be unavailable until you are typing inside a text frame
(§5 below); the two picture items until a photo is chosen.

**3.4 Insert menu** (`Alt+I`): Add a text frame (`Ctrl+Shift+T`) · Insert a picture
(`Ctrl+Shift+P`) — then, after a separator — Lodge officers · Birthdays · Bring in birthdays from
the address book (`Ctrl+Shift+U`) · Committees · District calendar · Announcement box · Cover
heading. The six widgets carry no shortcut of their own; the menu is the only path to each. All of
them should be unavailable until a document is open.

**3.5 Arrange menu** (`Alt+R`): Wrap text around this frame (`Ctrl+Shift+W`) · Continue this text in
another frame (`Ctrl+Shift+L`) · Stop continuing into the next frame (`Ctrl+Shift+K`) · Make the
rest of this text fit (`Ctrl+Shift+M`) · Fit this item to its contents (`Ctrl+Shift+Y`) · Bring
forward (`Ctrl+]`) · Send backward (`Ctrl+[`) · Bring to front (`Ctrl+Shift+]`) · Send to back
(`Ctrl+Shift+[`) — then, after a separator, the eight M21 commands that act on **more than one**
thing: Line up the left edges · Line up the centres, side to side · Line up the right edges · Line
up the top edges · Line up the middles, top to bottom · Line up the bottom edges · Space them out
evenly, side to side · Space them out evenly, top to bottom. Everything here is about **where a
thing sits on the page**, which is why the four stacking commands are one level up from where M11
left them. The eight new ones carry **no shortcut**: the menu is their keyboard path, and each
should announce as unavailable — with a reason naming Shift+click — until two things are chosen
(three, for the two spacing commands).

**3.6 View menu** (`Alt+V`): Zoom in (`Ctrl+=`) · Zoom out (`Ctrl+-`) · Actual size (`Ctrl+0`) ·
Fit page (`Ctrl+1`) · Show or hide the panel of things you can do · Show where fonts were changed ·
Move to the next part of the window (`F6`) · **Move to the previous part of the window
(`Shift+F6`)** · Change how things look. Shift+F6 gained its menu item in M17; before that it was
the one gesture in the whole app that could only be found by guessing.

**3.7 Page menu** (`Alt+P`): Next page (`Ctrl+Page Down`) · Previous page (`Ctrl+Page Up`) · Add a
page after this one · Delete this page · Move this page earlier · Move this page later.

**3.8 People menu** (`Alt+L`): Open your lodge address book (`Ctrl+Shift+R`) · Import members from a
spreadsheet · Save your address book as a spreadsheet · Undo the last change to your address book ·
Restore an earlier version of your address book. The address book's undo is a **separate sentence**
from the newsletter's, because `Ctrl+Z` never crosses that boundary — check that the two are not
confusable by ear.

**3.9 Help menu** (`Alt+H`): Check for an update · Show me an example newsletter · Fonts and
licences · About TrestleBoard.

**3.10 Toolbar, left to right.** Nine controls, not eighteen: everything that acts on the thing you
have chosen moved into the panel in M11, where it sits beside that thing. With the sample newsletter
open, `Tab` across the row and write down what each announces: Open a newsletter (`Open`) · Undo ·
Redo · Previous page (`Back`) · **Current page** label · Next page (`Next`) · Zoom out (`Smaller`) ·
**Current zoom** label · Zoom in (`Bigger`) · Fit the whole page in the window (`Fit page`).
**PASS criterion:** every button announces role "button" plus the plain-language name above — not
just the short label that's visibly printed on it (e.g. you should hear "Zoom out," not "Smaller,"
though exact renderings vary by screen reader).

**3.11 Unavailable items must say why.** Pick any greyed menu item — Export as PDF with nothing
open, or Fix this picture with no photo chosen — and read its **help text** as well as its name
(NVDA: `Insert+Tab`, or listen to the full announcement). Since M11 every unavailable command
carries a plain-language reason there, and pressing its shortcut says the same sentence in the
status line. "Dimmed" with no reason is a finding.

**3.12 Status line.** Select a frame that overflows its text (see §6/§5 for how to make one, or use
the sample document and look for a red `+` badge), and check whether the status line — labelled
"Status" — speaks its text automatically without you having to Tab to it. It is marked as a
"polite" live region in the source, but live-region support varies by screen reader/OS
combination; write down whether it announced itself or whether you had to go find it.

---

## 4. The canvas: tabbing between blocks

> **Read this before you run the section.** `docs/M9-spec.md` §5 and PLAN.md §6 promise that the
> page canvas exposes each block to the screen reader by name — "Text frame: …," "Photo: …," a
> widget's display name, "Decoration" for a shape. **That code does not exist in this build** (no
> `PageCanvasAutomationPeer` anywhere in the source — see Finding 2). The canvas is one opaque
> control named "Newsletter page editor." This section is expected, on the evidence read while
> writing this script, to fail. Please run it anyway and write down exactly what (if anything)
> is announced — "nothing was announced" is the correct and useful answer if that's what happens.

**4.1** With the sample newsletter open, build a small test page: use Insert ▸ Lodge officers to
add a widget (cancel out of the wizard once the box appears on the page), Insert ▸ A picture… to
add a photo (see §7 for the description prompt), and Insert ▸ A text frame to add
an empty text frame. **From M17 the caret lands in the new text frame straight away**, so press
Escape once to get back to frame selection before you go on. You now have at least three different kinds of block on one page. (There is
no menu path to the pre-built five-page sample with a full mix of content — it exists only for
the automated tests — so building a small page by hand like this is currently the only way to get
a mixed page in front of a screen reader.)

**4.2** Click once on the empty grey area around the page (not on any block) to make sure nothing
is selected, then Tab until focus reaches "Newsletter page editor."
Expected: NVDA/VoiceOver says something containing "Newsletter page editor."

**4.3** With the canvas focused, press `Tab` again.
**PASS criterion:** the screen reader should say the name of the block that is now selected —
for example "Text frame: Write the story here" or "Photo: [your description]" or the widget's
display name ("Lodge officers"). Write down exactly what you hear. If you hear nothing at all
(the visible selection outline moves but nothing is spoken), that confirms the gap described
above — write "nothing announced, selection moved silently" rather than leaving it blank.

**4.4** Repeat `Tab` several more times to cycle through every block on the page, and `Shift+Tab`
to go backward.
Expected/PASS criterion: same as 4.3, once per block, in both directions.

**4.5** Press `Escape` to clear the selection.
Expected: whatever was announced for the selected block should stop being implied — again, note
whether anything is said at all.

---

## 5. Editing text in a frame

**5.1** Tab to a text frame on the canvas (or click it), then press `Enter` (or `F2`) to start
editing.
Expected: this switches from frame-selection mode into text-editing mode. There is no separate
accessible control for "the text inside the frame" — the whole page is one custom-drawn surface —
so **do not expect NVDA/VoiceOver to announce "editing" or read the frame's text back to you.**
Write down whatever is or isn't said.

**5.2** Type a short sentence.
Expected/PASS criterion: this is the sharpest edge of the gap described in §4 — because the canvas
paints its own text instead of using a native text box, your screen reader has no way to read
character-by-character or word-by-word feedback as you type, and no way to announce what you just
typed. Confirm this is in fact what happens (or, if your screen reader surprises you and does
say something, write down exactly what).

**5.3** Press `Escape`.
Expected: you leave text-editing mode and the frame becomes the selected frame again (per
`docs/M5-spec.md` §1.2, this is the documented, intentional route from typing back to frame
manipulation). Note whether anything is announced.

**5.4** Select the text you typed (`Ctrl+A` selects the whole story while editing) and press
`Ctrl+B` to make it bold.
Expected: the Bold button/menu item should now show as "checked"/"pressed" if you Tab to it. Note
whether your screen reader announces the toggle state.

---

## 6. Running the Officers wizard end to end

This is the accessibility centerpiece of the whole application (PLAN.md §6) — read every step.

**6.1** With a document open, choose Insert ▸ Lodge officers.
Expected: a new window opens immediately (per `docs/M7-spec.md` §7's "insert puts an EMPTY widget
on the page and opens its wizard straight away" rule) — you do not need to separately select the
new widget on the canvas first. Write down what is announced when the window opens; it should
contain "Lodge officers" and a "Step X of Y" progress phrase (per the source, the officers wizard
runs to 14 screens: a first screen plus one per each of the twelve officer positions plus a final
review screen).

**6.2** Without pressing Tab, press `Alt+N` (Next) a couple of times.
**PASS criterion:** each time, the screen reader should automatically announce the new screen's
heading — you should not have to Tab to a control to find out the question changed. (The source
comment for this says explicitly: "Avalonia has no live region, and this is the only reliable way
to make Narrator/VoiceOver announce the new question" — the header is silently focused and
unfocused on every screen change specifically to produce this announcement.) Write down whether
it worked and what was said.

**6.3** On one of the officer screens, leave the phone number field but type something invalid
(e.g. letters instead of digits) into it, then press `Alt+N`.
**PASS criterion:** an error message appears and should be announced. Per the source it reads
something like *"⚠ Check this — Phone numbers look like 555-0100. Please check this one."* — a
complete, plain-language sentence, not the word "invalid" or "error." Confirm the wording you
hear is a real sentence, not a technical message.

**6.4** Continue through to the review screen (use `Alt+N` repeatedly, or `Ctrl+Enter` to jump
straight there once everything validates).
Expected: a read-only list of what will be saved, and **every single line should have its own
"Change this" button** right next to it. Tab through a few lines and confirm each has an adjacent,
separately-focusable "Change this" button (not one shared button for the whole screen).

**6.5** Activate "Change this" on one of the lines partway down the list.
Expected: you should land back on the exact screen for that item, not screen 1.

**6.6** Return to the review screen and note the primary button's label.
Expected: it should read "Save it," not "Next" or "Finish."

**6.7 — Known gap, test it anyway.** Go back to Insert ▸ Lodge officers again (a fresh widget),
type something into the first field, then press `Escape`.
**Expected per the written spec (`docs/M7-spec.md` §6.6):** a plain-language confirmation —
*"Throw away what you typed? Nothing has been added to the newsletter yet."*
**What the code currently does (verified while writing this script):** the wizard window closes
immediately with no confirmation of any kind. **This step is expected to fail.** Confirm that is
what happens (the window just closes) and record it — this is Finding 6 below, and this step is
how you can reproduce it.

**6.8** Once you've saved an officers list, Tab to the Edit menu and read the Undo item.
Expected: it should read something like "Undo Edit lodge officers" — the plain-language
description PLAN.md §4 requires, not a generic "Undo."

---

## 7. Inserting a photo, including the description prompt

**7.1** Choose Insert ▸ A picture… (`Ctrl+Shift+P`).
Expected: the operating system's file picker opens (again, not TrestleBoard's own accessibility —
confirm you can navigate it, then choose any JPEG or PNG).

**7.2** After choosing a file, a "Describe this picture" window should open.
**PASS criterion:** the description text box should already have keyboard focus — you should be
able to start typing immediately with no extra Tab press. Confirm this, and write down the
announced name of that box; per the source it should be **"Description of the picture."**

**7.3** Tab to the second text box.
Expected name: **"Caption printed under the picture."** The visible label above it should read
"Caption (optional)" — confirm the word "optional" is conveyed somehow (either spoken directly, or
readable from context), since this field, unlike the description, is not required.

**7.4** Type a (fictional) description — for example "Two officers at a Placeholder Lodge social"
— and Tab to the "Put it on the page" button, then activate it.
**Observation to record, not a strict pass/fail:** try this a second time leaving the description
completely blank, and note whether the app stops you. As written today the "Put it on the page"
button does not require the description box to contain anything — nothing currently prevents an
undescribed photo from reaching the page. Write down what you observe; see Finding 10.

**7.5** With the new photo selected on the canvas (Tab to it, per §4's caveats), choose Format ▸ Picture ▸
Fix this picture (`Ctrl+Shift+F`).
Expected: the status line should announce something like *"Picture fixed. Press Ctrl+Z if you
liked it better before."* Confirm whether this is spoken without you having to go find it (it is
the same "polite" status line from §3.9).

**7.6** Choose Format ▸ Picture ▸ Adjust the picture… (`Ctrl+Shift+A`).
Expected: a window titled "Adjust the picture" opens with three sliders — **"Brighter or darker,"
"More or less contrast," "More or less colour"** — plus **"Turn the picture a quarter turn"** and
**"Undo all changes to this picture"** buttons. Tab through all five controls and record what is
announced for each; write down the window's own announced name too (compare it against the
visible title "Adjust the picture" — this window does not explicitly set an accessible name in the
source, so what you hear may fall back to the title text, which is worth confirming either way).

---

## 8. The grid re-editor

**8.1** Select a widget that has a list inside it (Lodge officers, Birthdays, or Committees all
qualify), then choose Edit ▸ Edit the list… (`Ctrl+Shift+G`).
Expected: a window titled "*[widget name]* — edit list" opens, with every row on one scrolling
page instead of one row per screen.

**8.2** Tab through several rows.
Expected: each field is individually labelled and focusable, the same as in the step-by-step
wizard (§6) — for example a phone number box should still announce "Phone" or the field's label,
not just "edit box."

**8.3** If the widget you chose allows adding/removing rows (Birthdays and Committees do;
Officers does not — its twelve positions are fixed), find the "Add another"/"Remove" buttons and
confirm they are reachable by Tab and clearly named.

**8.4** Select the Cover heading widget instead (Insert ▸ Cover heading) and check whether Object
▸ Edit this list… is available at all.
Expected: it should be greyed out/unavailable, since a cover heading has no list inside it —
confirm your screen reader announces it as unavailable rather than silently doing nothing when
activated.

---

## 9. Exporting a PDF

**9.1** With a document open, choose File ▸ Export as PDF… (`Ctrl+E`).
Expected: the operating system's save-file dialog opens; give it a name and save.

**9.2** After the save dialog closes, listen carefully.
**Observation to record:** as written today, nothing in the app confirms the export succeeded —
no status-line message, unlike Fix Photo in §7.5. The only feedback is that the save dialog itself
closed (which the operating system announces, not TrestleBoard). Confirm whether this matches
what you experience, and write down anything you do hear.

**9.3** Try exporting again but cancel the save dialog instead of saving.
Expected: nothing happens; no error, no PDF written.

**9.4** If you can arrange a failure (for example, opening the exported PDF file in another
program first, on an OS where that locks the file, then exporting to the same name again): a
dialog titled "Could not export the PDF" should appear with a plain-language explanation and an
"OK" button. Confirm the message is a full sentence in plain language, not a raw exception.

---

## 10. Crash recovery: the restore dialog

> **This section cannot currently be executed. There is nothing to test.** `docs/M9-spec.md` §1
> specifies a `RecoveryService` that autosaves every 60 seconds (or 5 seconds after you stop
> typing) and, on the next launch after a crash, shows a restore dialog with a thumbnail of page 1.
> The `RecoveryService` class itself exists and has its own automated tests
> (`src/TrestleBoard.Editing/RecoveryService.cs`), but **nothing in `TrestleBoard.App` ever
> constructs or uses it** — there is no autosave running, no file written to
> `<AppData>/TrestleBoard/recovery/`, and no restore dialog anywhere in the source. See Finding 3.

**10.1** For the record, try it anyway: open the sample document, type something, then force-quit
the app from your OS's task manager (do not use File ▸ Exit — that is a clean close and won't
demonstrate anything either way).

**10.2** Relaunch the app.
**Expected result today:** the app opens with no document loaded and no mention of the work you
just lost — because nothing was ever saved for recovery. Confirm this is what happens, and record
it as "not implemented" rather than Pass or Fail in the results table — a Fail would imply a
recovery dialog appeared and did something wrong, and that is not what's being observed here.

---

## 11. The panel of things you can do, at 200% and in High Contrast (M11)

> **Do this whole section twice**: once with the app as it comes, and once after setting
> View ▸ How things look… to **High contrast** and the size to **200%**. PLAN.md §11-M11 names
> two things here as the ones that must be validated by ear rather than by test — the panel
> heading as a live region, and greyed menu items that carry a spoken reason — so please be
> especially exact about what you hear in 11.3 and 11.6.

**11.1** Open the sample newsletter (File ▸ Open the sample newsletter). Look at the right-hand
side of the window.
**What you should see:** a panel headed "Nothing is selected", listing what this newsletter still
needs ("What's next") and the ways of putting something new on the page.

**11.2** Press **F6** repeatedly.
**What you should hear:** the focus moving between the page, the panel, the toolbar and the menus,
and the status line saying "Moved to the panel of things you can do", and so on. Shift+F6 goes the
other way. Write down whether the screen reader announces the move at all, or only the control
that received focus.

**11.3** Put the focus on the page and press **Tab** until a photo is selected.
**What you should hear:** the panel's heading changing to **"A photo is selected"**, spoken
without you having to go looking for it. This is the live region. If you hear nothing until you
Tab into the panel, that is the finding — write down exactly that.

**11.4** With the photo still selected, move through the panel with Tab.
**What you should hear:** "Fix this picture", "Adjust the picture", "Wrap text around this",
"Delete this", and the front-and-back actions. **You should not hear the word "dimmed" or
"unavailable" anywhere in this panel.** If you do, that is a failure — nothing in the panel is
allowed to be greyed.

**11.5** Select a text frame instead of the photo.
**What you should hear:** the heading becomes "A text frame is selected", and the picture actions
are **gone from the panel entirely** rather than dimmed. That absence is deliberate: the panel is
headed with what is selected, so an action that is missing reads as "not about text frames".

**11.6** Now open the **Arrange** menu with nothing selected on the page (press Escape on the
canvas first).
**What you should hear:** several items announced as dimmed or unavailable — that part is normal
and deliberate in a menu. What is being tested is whether your screen reader also reads the
**reason**, which is attached to each item as help text, for example "This needs a picture. Choose
one on the page first." Write down verbatim whether the reason is spoken, and whether you had to
do anything extra to hear it (NVDA: object navigation, or the "report object description" key).

**11.7** With nothing selected, press **Ctrl+Shift+F** (Fix this picture).
**What you should hear:** the status line saying "This needs a picture. Choose one on the page
first." Nothing should happen to the newsletter. Silence here is a failure.

**11.8** Put the focus on the canvas, select any block, and press the **Applications key** (or
Shift+F10).
**What you should hear:** a menu of the things you can do to that block. Before M11 this key did
nothing at all over the canvas.

**11.9** Make the window narrower than about 900 pixels wide.
**What you should see:** the panel folding away to a single "What can I do? ▸" button, rather than
squeezing the page into a strip. Widen it again and the panel comes back.

## 12. The address book, keyboard-only (M12)

> PLAN.md §11-M12's acceptance ends *"all of it completable keyboard-only"*. Every screen is built
> from list boxes, combo boxes and buttons with nothing to drag, and the headless suite drives the
> import session end to end — but "operable" and "usable without a mouse by somebody who is not the
> person who built it" are different claims, and only this section can settle the second.
>
> **Do not run this section with the real lodge roster loaded.** Import the fictional
> `tests/Roster.Tests/Fixtures/members-100.csv` into a copy of the app first, or run it on a machine
> whose address book is empty. Everything you write down here may be read by somebody else
> (PLAN.md §0 rule 5).

**12.1** With no mouse, press **Alt** and then **L** to open the People menu.
**What you should hear:** "People menu", then its five items. Note whether "Save as a spreadsheet"
is announced as dimmed, and whether the reason — "Your address book is empty, so there is nothing to
save yet…" — is spoken with it.

**12.2** Press **Ctrl+Shift+R**.
**What you should see and hear:** the People window opening with the focus already in the search box,
and the box announced as "Search for a person by name".

**12.3** Type three letters of a surname.
**What you should hear:** the result count spoken without you moving focus — "7 people match
"pla"." This is a live region, and it is the one thing in this window that must be validated by ear.
If you hear nothing until you Tab to the list, write down exactly that.

**12.4** Tab to the list and move through it with the arrow keys.
**What you should hear:** each row as one phrase — "A. Placeholder — Worshipful Master — 7/4" —
rather than a name alone.

**12.5** Tab on into the form, change the telephone number, and press the **Save this person**
button with Space.
**What you should hear:** "A. Placeholder was saved." Write down whether you heard it without
hunting for it.

**12.6** Press the **Remove this person…** button.
**What you should hear:** the confirm, including the sentence "Newsletters you already made will not
change." Choose **No, keep them** with the keyboard and confirm nothing was removed.

**12.7** Close the window, then use People ▸ **Import from a file…** and walk the whole flow with
the keyboard alone, using the fictional 100-person fixture. At each screen, write down whether the
heading was spoken when it changed (it is a live region) and whether Tab reached every control.
The mapping screen is the one to watch: seven questions, each a combo box, each item reading as
"C — Phone (555-0101, 555-0102)".

**12.8** On the review screen, read what it says aloud into your notes. It should say in plain
counts what will happen, and the button should say **Add these people** rather than OK.

**12.9** Finish the import. **What you should hear:** "Your address book now has 100 people."

**12.10** Use People ▸ **Undo the last change**. Note whether the menu item named what it would take
back before you pressed it ("Undo Import people from a file"), and whether the address book went
back to what it was.

---

## 13. Birthdays and officers from the address book, keyboard-only (M13)

You need the fictional address book from section 12 loaded before you start, and a newsletter open
whose issue month matches at least one birthday in it.

**13.1** With nothing selected, look at the panel's **What's next** card. If the birthday list is out
of date it leads with **Update the birthday list**. Reach that button with the keyboard alone
(F6 to the panel, then Tab) and press it.
**What you should hear:** the reason sentence beneath it, then the diff window's heading.

**13.2** On the diff window, Tab through everything. It should read as headings and names — "These
will be added (7)", then each name and date — followed by the sentence "Nothing changes until you
press Update the list."
**What you should hear:** nothing that sounds like a status code or a count of records.

**13.3** Choose **Leave it as it is**. Confirm the newsletter did not change: the status line should
say so, and Ctrl+Z should still take back whatever you did before this.

**13.4** Do it again and choose **Update the list**.
**What you should hear:** a plain sentence in the status line saying what changed and that Ctrl+Z
takes it back. Press Ctrl+Z and confirm the list goes back exactly as it was, in one step.

**13.5** Select the birthday list on the page and press `Ctrl+Shift+U`. This is the same action from
the other direction.
**What you should hear:** either the diff window again, or — if it is already up to date — "The
birthday list already matches your address book."

**13.6** Empty the address book (or open a newsletter whose month nobody was born in) and press
`Ctrl+Shift+U` again. **The point of this step is the refusal.** It must say why, in a sentence, and
point somewhere useful.

**13.7** Insert a fresh birthday list from the Insert menu with the address book loaded.
**What you should hear:** the extra first screen — "We found N birthdays in <month> in your address
book. Add them?" — before the wizard's own questions. Answer **No, I'll type them myself** once and
confirm the wizard is exactly as it was before M13.

**13.8** Run the officers wizard. On the Worshipful Master's screen, type three letters of a name
from the address book.
**What you should hear:** whether the suggestion list is announced at all, and how. This is the one
control in the app whose screen-reader behaviour has never been observed — write down verbatim what
happens, even if it is nothing.

**13.9** Choose a name from the suggestions and Tab to the phone box.
**What you should hear:** the phone number already filled in from the address book. Then type a
different number over it.

**13.10** Continue to the review screen. Under **While we are here** there should be one checkbox
asking whether to update that person's phone number in the address book. Confirm it is **not**
ticked, tick it with the keyboard, and finish.
**What you should hear:** after saving, a sentence saying the address book was updated too.

**13.11** Type a name that is **not** in the address book into an officer's Name box and finish the
screen. This must simply work — the picker is never a dead end.

**13.12** Edit a committee and use **Add someone from the address book…**. Reach it, search with
three letters, and add somebody, all from the keyboard.
**What you should hear:** the spoken result count as you type, and the name appearing as a new line
in the members box.

## 14. Choosing a font, keyboard-only (M14)

Have a newsletter open with some writing in it. Everything in this section must be reachable
without a mouse.

**14.1** Press `Ctrl+Shift+D`, or reach **Format → Fonts and text styles…** with Alt.
**What you should hear:** the window's name, "Fonts and text styles", and then the first list.

**14.2** Tab to the list on the left. Arrow down it.
**What you should hear:** plain role names — "Body text", "Headings", "Photo captions" — followed by
the font and size each one uses. **You must never hear `body-bold-italic` or any other raw style
name.** If you do, that is a failure, not a curiosity.

**14.3** Tab to the search box and type three letters of a family, e.g. `gar`.
**What you should hear:** the number of matching fonts spoken as you type. Clear the box again and
confirm the full count comes back.

**14.4** Tab into the font list and arrow through it.
**What you should hear:** the family name, then its one-sentence description. The group headings
("Fonts for reading", "Fonts for titles", …) should be reachable or announced in some form — write
down which.

**14.5** Tab to **− Smaller** and **+ Bigger** and press each once.
**What you should hear:** the new size, in points, each time. The buttons walk a fixed ladder, so you
should never hear a size like "11.3 pt".

**14.6** Tab on to the reflow warning and read it.
**What you should hear:** a sentence naming the kind of writing that will change, and saying the page
count may move and that Ctrl+Z puts it back. Then the sentence "Nothing changes until you press
Apply."

**14.7** Press **Apply**.
**What you should hear:** a sentence in the status line saying what changed, and — if the newsletter
is now a different length — how many pages it has now.

**14.8** Press Ctrl+Z **once**.
**What you should confirm:** everything goes back, in one step. Bold and italic writing must go back
too, not just the plain text.

**14.9** Click (or Tab) into some writing, highlight a few words, and use **Format → Use a different
font just here…**. Choose a different family and Apply.
**What you should hear:** a sentence saying those words now use their own font.

**14.10** With the caret still in those words, listen to the panel of things you can do (F6).
**What you should hear:** the sentence "This text uses <family> instead of the <role> font", and a
button offering to put it back. Press it and confirm the words go back.

**14.11** Turn on **View → Show where fonts were changed**.
**What you should hear:** how many pieces of text are marked, and that the marks never print. Turn it
off again and confirm you are told.

**14.12** Open **Help → Fonts and licences**.
**What you should confirm:** the licence text is readable, scrollable and selectable from the
keyboard, and the window can be closed with Escape. **This is a licence obligation, not a
nicety** — the fonts ship with the app and the OFL requires their licence to ship with them.

**14.13** Open a newsletter made by a build with more fonts than this one, if you have one.
**What you should hear:** one plain sentence at open saying a font is missing, what will be shown
instead, that the newsletter itself is not changed, and that a newer TrestleBoard will show it
properly. No dialog, no error, no stack trace.

---

## 15. Finding words, and lining things up (M21)

Have the example newsletter open (**Help → Show me an example newsletter**). Everything in this
section must work without a mouse.

**15.1** Press `Ctrl+F`.
**What you should hear:** the window's name, "Find window", and then the box named "What are you
looking for?". The window is **not modal** — the newsletter behind it stays reachable, which is
deliberate, because this window exists to point at something in it.

**15.2** Type `lodge` and press Enter.
**What you should hear:** a sentence saying what was found and how many there are altogether, from
the live region inside the window **and** from the status line at the bottom of the main window.
Confirm the newsletter behind has scrolled to the word and highlighted it.

**15.3** Press Enter again, several times, until the search wraps.
**What you should hear:** "Carried on from the beginning" the first time it goes round. It should
never go silent.

**15.4** Search for something that is not there, e.g. `zamboni`.
**What you should hear:** that the words are not in the writing on the page, **and** that
TrestleBoard does not look inside the lists it fills in for you. The second half matters: without it
the sentence teaches you something false about the officers table.

**15.5** Close the find window with Escape, then press `Ctrl+H`.
**What you should hear:** the same window, now named "Find and replace window", with a second box
named "What should go there instead?".

**15.6** Replace every one of something, then press `Ctrl+Z` **once**.
**What you should confirm:** the whole replacement goes back in one step, and the Edit menu's Undo
item said "Undo Replace all" before you pressed it.

**15.7** Click into a text frame and start typing, then Tab or F6 away to the panel or the menu bar
without pressing Escape first.
**What you should confirm:** the caret and anything you had highlighted are **still there** when you
come back. Before M21 focus loss ended the session silently, and this step is the check that it no
longer does.

**15.8** Choose something on the page, then hold **Shift** and click a second and a third thing.
**What you should hear:** the panel heading change to "3 things are selected", and a sentence
saying Shift+click adds or removes one.

**15.9** With three things chosen, open **Arrange** and choose "Line up the left edges".
**What you should hear:** a sentence in the status line saying what was done and how many things
moved. Press `Ctrl+Z` **once** and confirm all three go back together.

**15.10** With only ONE thing chosen, open Arrange again and read the eight new items.
**What you should hear:** each announced as unavailable, with a reason that tells you to hold Shift
and click another one. None of them may be silent about it.

**15.11** Hold **Ctrl** and turn the mouse wheel over the page (this one step needs a mouse).
**What you should confirm:** the zoom percentage in the toolbar changes, and the part of the page
under the pointer stays under the pointer. Then hold **Space** and drag, and confirm the view pans
without anything on the page moving — press `Ctrl+Z` afterwards and confirm there was nothing to
take back.

---

## Results table

Copy this table (or the row shape) into a spreadsheet or a copy of this file as you go. Use the
step numbers from above so the full "what you should hear" text doesn't need to be retyped.

| Step | Screen reader + version | What you heard (verbatim, from the Speech Viewer / Caption Panel) | Pass / Fail / N/A / Not implemented / Observed | Notes |
|---|---|---|---|---|
| 1.1 | | | | |
| 1.2 | | | | |
| 1.3 | | | | |
| 2.1 | | | | |
| 2.2 | | | | |
| 2.3 | | | | |
| 2.4 | | | | |
| 3.1 | | | | |
| 3.2 | | | | |
| 3.3 | | | | |
| 3.4 | | | | |
| 3.5 | | | | |
| 3.6 | | | | |
| 3.7 | | | | |
| 3.8 | | | | |
| 3.9 | | | | |
| 4.2 | | | | |
| 4.3 | | | | |
| 4.4 | | | | |
| 4.5 | | | | |
| 5.1 | | | | |
| 5.2 | | | | |
| 5.3 | | | | |
| 5.4 | | | | |
| 6.1 | | | | |
| 6.2 | | | | |
| 6.3 | | | | |
| 6.4 | | | | |
| 6.5 | | | | |
| 6.6 | | | | |
| 6.7 | | | | expected to fail — see Finding 6 |
| 6.8 | | | | |
| 7.2 | | | | |
| 7.3 | | | | |
| 7.4 | | | | see Finding 10 |
| 7.5 | | | | |
| 7.6 | | | | |
| 8.1 | | | | |
| 8.2 | | | | |
| 8.3 | | | | |
| 8.4 | | | | |
| 9.1 | | | | |
| 9.2 | | | | see Finding 7 |
| 9.3 | | | | |
| 9.4 | | | | |
| 10.1 | | | | |
| 10.2 | | | | mark "Not implemented" — see Finding 3 |
| 11.1 | | | | |
| 11.2 | | | | |
| 11.3 | | | | the panel heading as a live region — PLAN.md §11-M11 flags this as needing real-screen-reader validation |
| 11.4 | | | | |
| 11.5 | | | | |
| 11.6 | | | | greyed menu + spoken reason — the other flagged uncertainty |
| 11.7 | | | | |
| 11.8 | | | | |
| 11.9 | | | | |
| 12.1 | | | | |
| 12.2 | | | | |
| 12.3 | | | | |
| 12.4 | | | | |
| 12.5 | | | | |
| 12.6 | | | | |
| 12.7 | | | | |
| 12.8 | | | | |
| 12.9 | | | | |
| 12.10 | | | | |
| 13.1 | | | | |
| 13.2 | | | | |
| 13.3 | | | | |
| 13.4 | | | | |
| 13.5 | | | | |
| 13.6 | | | | |
| 13.7 | | | | |
| 13.8 | | | | |
| 13.9 | | | | |
| 13.10 | | | | |
| 13.11 | | | | |
| 13.12 | | | | |
| 14.1 | | | | |
| 14.2 | | | | |
| 14.3 | | | | |
| 14.4 | | | | |
| 14.5 | | | | |
| 14.6 | | | | |
| 14.7 | | | | |
| 14.8 | | | | |
| 14.9 | | | | |
| 14.10 | | | | |
| 14.11 | | | | |
| 14.12 | | | | |
| 14.13 | | | | |
| 15.1 | | | | |
| 15.2 | | | | the find window's live region and the status line, together |
| 15.3 | | | | |
| 15.4 | | | | |
| 15.5 | | | | |
| 15.6 | | | | |
| 15.7 | | | | the M17 deferral, closed in M21 |
| 15.8 | | | | |
| 15.9 | | | | |
| 15.10 | | | | |
| 15.11 | | | | the one step in this script that needs a mouse |

Add rows for anything else you notice along the way, even if it isn't in a numbered step.

---

## Reporting failures

For anything marked Fail (or anything surprising even if you weren't sure how to mark it):

1. File an issue in the project's GitHub repository (or hand a written note to whoever is running
   the milestone if you don't have GitHub access) with the label `accessibility` if that label
   exists.
2. Include, at minimum:
   - The step number (e.g. "4.3").
   - The exact utterance you heard, or "nothing was announced" if that's what happened — copy it
     out of the Speech Viewer / Caption Panel rather than paraphrasing from memory.
   - What you expected to hear (copy it from this document).
   - Screen reader name **and version number** (Help ▸ About in NVDA; VoiceOver's version tracks
     the macOS version — include that).
   - Operating system and version.
   - Whether you could reproduce it a second time.
3. Do not soften or summarize the utterance — "it didn't sound quite right" is not fixable;
   "NVDA said 'button' with no name" is.

---

## A note on Linux/Orca results specifically

Per PLAN.md §6, Linux AT-SPI support in Avalonia is explicitly **best-effort — the weakest of the
three targets, not something this project promises to a specific standard.** If you ran this
script under Orca, please still fill in the results table, but grade those rows "Observed" rather
than Pass/Fail, and mention them separately when you report back. A gap on Linux that doesn't
exist on Windows/macOS is useful information; it is not being held to the same bar as those two.

---

## Problems found while writing this script

These were found by reading the actual source while preparing the steps above, not by running the
script (which, per the note at the top of this document, has not happened yet). They are listed
here because several of them determine what a tester can and cannot do, and because a couple are
concrete, reproducible bugs rather than missing features.

1. **`Ctrl+Shift+Y` ("Fit to contents") is dead — shadowed by `Ctrl+Y` (Redo).**
   `src/TrestleBoard.App/MainWindow.axaml.cs`, `OnWindowKeyDown`, has `case Key.Y when ctrl:`
   (Redo) at roughly line 556, and a separate `case Key.Y when ctrl && shift:` (Fit to contents)
   at roughly line 643. C# evaluates `switch` cases in order, and the Redo case's guard is only
   `ctrl` — it does not exclude Shift — so it matches `Ctrl+Shift+Y` too, before the later case is
   ever reached. Pressing the shortcut the Arrange menu itself advertises for "Fit this item to its
   contents" (`Ctrl+Shift+Y`) actually redoes the last undone action. The menu item still works
   fine by mouse/Enter; only the keyboard gesture is broken. This is exactly the kind of
   menu-advertises-a-gesture-the-handler-doesn't-implement bug the milestone's automated keyboard
   audit (`docs/M9-spec.md` §6) is meant to catch — worth adding a regression test for this
   specific pair of shortcuts.

2. **No custom automation peer for the canvas — this is the big one.** PLAN.md §6 and
   `docs/M9-spec.md` §5 both promise a `PageCanvasAutomationPeer` that exposes every block on the
   page as a named child: "Text frame: …," "Photo: …," a widget's display name, "Decoration" for a
   shape. Nothing named anything close to that exists anywhere in the source tree (searched the
   whole `src/` directory). `PageCanvasControl` currently exposes exactly one accessible element —
   itself, named "Newsletter page editor" via `AutomationProperties.Name` in the XAML.
   Tab-cycling block selection (`FrameEditorController.CycleSelection`, wired from
   `PageCanvasControl.HandleFrameModeKey`) changes only the on-screen outline and resize handles;
   `FrameEditorController.Select` sets `StatusMessage` to `null` on an ordinary selection (only an
   *overset* frame gets a status message) — so there is no accessible name, no role change, and no
   status-line announcement when a screen-reader user moves between blocks. Given the PLAN's own
   acceptance bar for this milestone is "NVDA reads every control on the main window," this is the
   single largest gap between what's promised and what's built: on the canvas, today, NVDA reads
   nothing as you move between blocks. Section 4 of this script is built to surface exactly this.

3. **Crash recovery is not wired into the app.** `RecoveryService`/`IRecoveryStore`
   (`src/TrestleBoard.Editing/RecoveryService.cs`) exist and are unit-tested in isolation, but
   `Program.cs`, `App.axaml.cs`, and `MainWindow.axaml.cs` never construct a `RecoveryService`,
   never call `Poll()`, and there is no restore dialog anywhere under `src/TrestleBoard.App`. No
   autosave currently runs; nothing is ever written to
   `<AppData>/TrestleBoard/recovery/`. Section 10 of this script documents this rather than
   pretending it can be tested.

4. **No start screen and no templates.** PLAN.md §7 and `docs/M9-spec.md` §2 describe a start
   screen with "Start from last month" / "Open a newsletter" / "Start from a template" tiles, and
   three embedded `.tboard` templates (Classic 414, Simple 4-page, 6-page with photos). Neither
   exists: `App.axaml.cs` constructs `MainWindow` directly with no start screen in front of it, and
   a repo-wide search for "Template" only turns up the pre-existing `isTemplate` flag on
   `TboardManifest` — no template resources, no gallery UI. Sections 1 and 2 above test the actual
   launch path instead of the one PLAN.md describes.

5. **No theme or UI-scale setting.** PLAN.md §6 and §9-M9 promise Light/Dark/true High-Contrast
   themes and a 100–200% UI-scale setting reachable from a Settings dialog. Neither `App.axaml`,
   `App.axaml.cs`, nor anything under `Dialogs/` contains a Settings window, theme resource
   dictionaries, or a scale mechanism. This script has no themes section for that reason — there
   is nothing yet to point a tester at.

6. **`WizardWindow`'s Cancel/Escape never confirms, contradicting the written spec.**
   `docs/M7-spec.md` §6.6 states plainly that Escape, when the session `IsDirty`, should show
   *"Throw away what you typed? Nothing has been added to the newsletter yet."* before discarding
   anything. In `src/TrestleBoard.App/Dialogs/WizardWindow.cs`, the Cancel button is built as
   `cancel.IsCancel = true` (which routes Escape to the same handler) wired to
   `(_, _) => Close()` — no dirty check, no confirmation dialog, for either Escape or the Cancel
   button. A tester who fills in several of the twelve officer fields and then presses Escape
   loses that work silently. Step 6.7 above is written specifically to reproduce this.

7. **PDF export succeeds silently.** `OnExportPdfClicked` in `MainWindow.axaml.cs` writes the file
   and returns without ever setting `StatusLabel.Text` or otherwise confirming success — contrast
   with `PhotoController.FixPhoto`, which sets a plain-language confirmation message
   ("Picture fixed. Press Ctrl+Z…"). A screen-reader user who successfully exports a PDF gets no
   accessible signal that it worked, only that the save dialog closed (an OS-level event, not an
   app one).

8. **Dialog windows are inconsistent about setting their own accessible name.**
   `WizardWindow` and `WidgetGridWindow` both explicitly call
   `AutomationProperties.SetName(this, …)` on themselves. `PhotoInsertDialog`, `PhotoAdjustWindow`,
   and the generic error dialog built inline in `MainWindow.ShowErrorAsync` do not — they set only
   `Title` and rely on whatever Avalonia's default window automation peer falls back to. This may
   turn out to be harmless (a `Title` fallback is plausible), but it was not verified either way
   while writing this script, since only a screen reader can settle it — steps 7.6 and 9.4 above
   ask the tester to check and record it either way.

9. **The one fixture with a realistic mix of block types is not reachable from the UI.**
   `MainWindow.OpenIssueSample()` loads a five-page fixture with a representative spread of text
   frames, photos, and widgets, but it is only ever called from the headless test suite — there is
   no menu item or button that reaches it. A human tester has to assemble an equivalent page by
   hand (as Section 4 instructs) before there's anything interesting to Tab through.

10. **The photo description prompt does not require a description.** PLAN.md §6 states
    "screen-reader users must not meet an unlabelled photo," and `docs/M6-spec.md` §6 says alt
    text "is prompted at insert" — but `PhotoInsertDialog`'s "Put it on the page" button has no
    validation on the description box; an empty string flows straight through
    `InsertPhotoFromFileAsync` into `PhotoController.InsertPhoto` with nothing stopping it. Nothing
    in the wording of the specs strictly requires the field to be *mandatory* (only "prompted"),
    so this is flagged as an open question rather than a clear-cut bug — but as built, it is
    possible to insert a photo with no accessible description at all, which undercuts the stated
    purpose of the dialog. Step 7.4 above asks the tester to confirm this and record it.
