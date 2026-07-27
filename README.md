# TrestleBoard

A purpose-built desktop editor for producing a Masonic lodge's monthly trestle board
newsletter — edit text, tables, and photos on a free-form page canvas, fill recurring
sections (officers, birthdays, committees, district calendar) with simple step-by-step
wizards, and export a distribution-ready PDF.

Built for Indian Land Lodge 414's trestle board committee, with accessibility for
elderly users as a first-class requirement.

![The TrestleBoard editor showing the front page of a newsletter: a cover heading, an article, and a photograph with the text flowing around it.](docs/images/hero-issue-page1.png)

## What it does

### Lay out the page

Blocks go anywhere on the page and body text flows around them. This is the whole reason
the app draws its own pages instead of using a word processor's: a photograph dropped into
the middle of an article pushes the words aside and they close up again behind it.

![A close view of a newsletter page where the paragraphs break around a photograph, leaving a clean margin down the side of the picture.](docs/images/text-wrap-around-photo.png)

### Write and format text

Click into any frame and type. Everything can be undone, including things that are usually
one-way — moving a photo, changing a whole list, changing the typeface of the newsletter.

Choosing a typeface is a picture of the typeface, not a list of names, and every sample is
drawn by the same engine that will draw the PDF. Twenty families ship with the app and no
font installed on your computer is ever used, which is what makes a page look the same on
every machine.

![The fonts and text styles window. On the left are the kinds of writing in this newsletter, such as Body text and Cover title; on the right a list of typefaces, each shown in its own face, and a preview of the user's own words.](docs/images/font-picker.png)

### Fill in the recurring lists

The officers table, the birthday list, the committees and the district calendar are not
typed as tables. They are filled in by a wizard that asks one question per screen in large
type, and lays the answers out for you.

![A wizard screen asking for one lodge officer's name and telephone number, in large type, with big Back and Next buttons underneath.](docs/images/wizard-officers-step.png)

Every wizard ends by reading back what it is about to write, so nothing lands on the page
unseen.

![The last screen of a wizard, listing back everything that was entered so it can be checked before anything is put on the page.](docs/images/wizard-review.png)

Once a list exists, the wizard is not the only way back in — the same information is
editable all at once, in rows big enough to read.

![The list editor, showing every lodge officer at once in large rows, with Add and Remove buttons beside them.](docs/images/grid-editor.png)

### Photographs

Photographs are never altered. The original file goes into the newsletter untouched and
every change — crop, straighten, brighten — is a recipe applied when the page is drawn, so
the same picture can be re-cropped from the original months later. There is one **Fix
photo** button for the common case, and three large sliders behind it for the rest.

![The photograph adjustment window: three large sliders for brighter or darker, more or less contrast, and more or less colour, above buttons to turn the picture a quarter turn or start over.](docs/images/fix-photo.png)

### Your address book

A brother's name is typed once. The lodge's member list is imported from the spreadsheet
the committee already keeps, and from then on the birthday list fills itself in and the
officers wizard offers names rather than asking for them.

![The people window, listing members of the lodge with their office and birthday, a large search box above them and a form on the right for correcting a detail.](docs/images/people-window.png)

Importing asks one question per piece of lodge information — *"Name: which column has
it?"* — instead of showing a grid of column headings and hoping. The answers are guessed
first, and nothing is written until the last screen.

![The import window asking which column of the spreadsheet holds each piece of information — name, birthday, telephone — with the guesses already filled in.](docs/images/import-columns.png)

### Export the PDF

One command produces the file you email to the lodge. It is drawn by the same code that
draws the screen, with real embedded fonts and selectable text.

![Three pages of the finished newsletter side by side as they appear in the exported PDF: the cover, the officers page, and the birthdays and committees page.](docs/images/pdf-page-spread.png)

## Built for the people who use it

The committee is mostly elderly, and that shaped the whole application rather than a
settings page at the end of it.

Choose something on the page and what you can do to it appears beside it, each with a
sentence saying what it is for. Nothing in that panel is ever greyed out: an action that
does not apply is simply not shown, and one that is blocked says why in plain words.

![The editor with a photograph selected. A panel down the right-hand side is headed "A photo is selected" and lists what can be done to it, each with a short explanation.](docs/images/action-panel-photo.png)

Every command has a keyboard path and a menu entry — dragging is always an accelerator,
never the only way. Text is 16 point at minimum and 18 to 20 in the wizards, hit targets
are at least 44 pixels, and every control has a name a screen reader can read.

There is a true high-contrast theme, and the whole interface scales to twice its size
without the page itself changing, because the page is a piece of paper and has its own
zoom.

![The same newsletter page shown in the high-contrast theme: white text on black chrome, with the page itself still white paper.](docs/images/high-contrast.png)
![The same newsletter page with the menus, buttons and panel text at twice their usual size, and the page itself unchanged.](docs/images/scale-200.png)

Both are two choices in one small window, and nothing else is in it.

![The how-things-look window, offering a theme and a size for the app's own text, both as large controls.](docs/images/settings.png)

Work is saved automatically every minute. If the machine is switched off mid-sentence, the
next launch offers the work back with a picture of the page so it is obvious what is being
restored.

![The restore window after an unexpected shutdown, offering the unsaved newsletter back with a thumbnail of its first page and the time it was last saved.](docs/images/restore-dialog.png)

## Each month

![The start screen, offering three large buttons: start from last month, open a newsletter you saved, or start from a ready-made layout.](docs/images/start-screen.png)

The usual month begins with **Start from last month**. The officers, committees and
district table carry across, the dates move on, and last month's articles are cleared so
this month's can be written.

What the issue still needs is then listed with nothing selected, one line each and a
sentence saying why — the birthday list still showing last month, an article still holding
its reminder text, a PDF not yet exported.

![The editor with nothing selected. The right-hand panel is headed "What's next" and lists the things this month's newsletter still needs, each with a sentence saying why.](docs/images/action-panel-whats-next.png)

To see a finished newsletter without making one, open **Help → Show me an example
newsletter**.

## Installing

Downloads for Windows, macOS and Linux are on the
[releases page](https://github.com/donaldsteele/TrestleBoard/releases).
**[docs/INSTALL.md](docs/INSTALL.md)** walks through it in plain language, including the
SmartScreen and Gatekeeper warnings that appear because the app is not code-signed. Installed
copies update themselves from that same releases page.

## How it works

- **Platforms:** Windows, Linux, macOS (.NET 10 + Avalonia)
- **Rendering:** custom SkiaSharp/HarfBuzzSharp layout engine shared by the on-screen
  editor and PDF export — what you see is exactly what prints
- **File format:** `.tboard` (zip container; originals of every photo kept losslessly)
- **Fonts:** bundled only, never the ones installed on your computer. Every face is a
  static instance, subsetted and recorded in a manifest with its SHA-256, which is what
  lets the same newsletter paginate identically on all three operating systems and in CI.

![A newsletter page showing a table of lodge officers with their positions and telephone numbers, selected on the page, with the article text flowing beside it.](docs/images/officers-table-widget.png)

## Building

Requires the .NET 10 SDK (see `global.json`).

```
dotnet build TrestleBoard.slnx
dotnet test TrestleBoard.slnx
dotnet run --project src/TrestleBoard.App
```

The screenshots in this file are generated, not taken by hand — see
[docs/images/README.md](docs/images/README.md).

See `PLAN.md` for the full architecture and milestone plan.

## Licence

All twenty bundled typefaces are used under the SIL Open Font License 1.1, with the
designers, upstream sources and pinned versions listed in [docs/FONTS.md](docs/FONTS.md).
The complete licence text ships inside the installer and is reachable from **Help → Fonts
and licences** — the OFL requires that of anything redistributing the fonts, and shipping
the fonts without it would not be enough.

The application's own source has no licence file yet. Until one is added, no permission to
copy, modify or redistribute the code is granted; the repository is published so the lodge
and its committee can build and audit what they run.
