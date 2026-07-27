# M16 — Colour, icons and visual polish

**Delivered 2026-07-27.** Implements PLAN.md §11 M16. This file records what was built, the
decisions that differ from the plan, and what is still open.

M16 was added on 2026-07-27 from the owner's review of the screenshots M15 committed: *"our UI is
starting to shape up but it still feels clunky and ugly"*, with two specifics — the side panel's
actions want grouping and labelling, and several panel buttons wrap their text.

**The case is not taste, and it was not argued as taste.** High Contrast was broken, and
`docs/images/high-contrast.png` showed it: the toolbar, the status bar and the area around the page
all stayed mid-grey with the theme on, putting a black button on `#6B6B6B` at **3.96:1** against §6's
7:1 requirement — for about a third of the window, while the overlay's own header claimed every
accent was checked at 7:1 or better.

---

## 1. The spike, first — does a custom `ThemeVariant` inherit Fluent's Dark resources?

The plan named this the single biggest unverified assumption and said to settle it before a single
icon was drawn. The whole milestone rests on it: collapsing the runtime `StyleInclude` overlay into
`ThemeDictionaries` means High Contrast has to be a variant in its own right, and a variant that did
not inherit would be missing several hundred FluentTheme control brushes.

**It holds on the pinned Avalonia 11.3.18.** With

```csharp
public static readonly ThemeVariant HighContrast = new("HighContrast", ThemeVariant.Dark);
```

`Application.TryGetResource(key, AppTheme.HighContrast, out _)` returns, for every FluentTheme key
tried, exactly the value that key has in Dark. `ThemeCompositionTests.HighContrastInheritsEvery
FluentResourceThePaletteDoesNotOverride` pins it with eight keys and a guard requiring at least four
of them to differ between Light and Dark — otherwise "it matches Dark" would prove nothing.

**The documented fallback was not needed** and is not in the tree: no `StyleInclude` survives.

Two smaller unknowns from the same spike:

- **Fluent's `SystemControl*` interiors did not need overriding.** The palette paints the surfaces
  the application itself owns — toolbar, status bar, panel, footers, the desk around the page — and
  Fluent's own control interiors read correctly against all three of them. Overriding them would
  have meant owning a control theme per control for no gain a user could see.
- **Half-pixel blur at 200% did not appear.** The glyphs are closed fills inside a `Viewbox`, not
  strokes, so a half-pixel landing softens an edge by a fraction of a pixel rather than dropping a
  1px line to grey. This is most of why the "closed fill, never a stroke" rule is worth its
  awkwardness.

## 2. The palette

`src/TrestleBoard.App/Theme/Palette.axaml` — one `ResourceDictionary` with all three variants as
`ThemeDictionaries`, merged from `Application.Resources`. `Settings/HighContrastTheme.axaml` is
deleted. `ThemeManager.Apply` now sets one property and nothing else.

**Tokens are named for the role they play, never the colour they hold.** The accent is navy
`#1E335C` on a light ground and a pale `#8FB4E8` on a dark one — navy is 1.68:1 on black and
structurally unusable there — and no key called "Navy" could have done that.

**Two of the three brand colours could not be used as `assets-src/icons/README.md` states them.**
The gold `#C8A24B` is **2.41:1** on white and the rule grey `#9AA5B8` is **2.49:1**: neither can
carry text, and the grey cannot carry a boundary that must be perceived (WCAG 1.4.11 asks 3:1). So
the gold became `Ornament.OnAccent` — *the key name stating its only legal background* — and the rule
grey was walked darker to `#6E7A8F` for real boundaries, surviving only as the decorative
`Chrome.Divider`. "Masonic blue" works as a chrome palette; "masonic blue *and gold*" on white chrome
does not.

**Two things differ from the plan, both discovered while doing the arithmetic:**

1. **Focus is two tokens, not one.** A single ring colour cannot clear 3:1 against both a near-white
   chrome background and the navy accent fill: 3:1 on near-white needs a relative luminance below
   about 0.17 and 3:1 on navy needs one above about 0.20. The ring is therefore drawn the way
   Fluent's own is — `Focus` outside, `Focus.Inner` inside, each contrasting with the other — so
   whatever it lands on, one of the two is visible. Three declared pairs cover it.
2. **`Rule.Top` and `Rule.Bottom` joined `Border.Thickness` and `Focus.Thickness`.** A uniform
   `Thickness` cannot express "a hairline along one edge", which is what a toolbar, a status bar and
   a footer each need. Like the other two they step up in High Contrast (1 → 2), so "every rule that
   changes a colour also changes a border" stays mechanical.

`Page.Sheet` is `#FFFFFF` in all three variants. Nothing paints with it: it is declared so that
`Page.Backdrop` has something its floor can be measured against, and because **the page never
themes** is worth saying in the file where somebody might be tempted to make it.

## 3. The gate

Three checks, and the first is the one that matters.

**`EveryDeclaredPairMeetsItsFloorAndTheWrittenRatioIsRight` parses `Palette.axaml` as a file.** It
reads the `PAIR … >= floor : Light … Dark … HighContrast …` block out of the header comment,
recomputes each ratio from the hex values declared below it, and fails on either a missed floor or a
stale number. **The ratios in the comment are the test's expected values, not documentation about
them** — the discipline the old overlay header practised by hand, mechanised. In High Contrast every
floor is raised to 7; pairs declared `decorative` carry no floor anywhere, which is how a hairline
nobody has to perceive avoids being quietly promoted into a boundary somebody does.

**The live-tree walk** runs over the same nine windows `AccessibilityTests.EveryWindow()` builds, in
all three variants. Its limits are written into its own summary: it sees what the styling system
*resolved*, not what the compositor *painted*, so where a control template paints a layer the logical
tree does not expose it skips rather than guesses. It also skips disabled controls (Fluent dims them
and WCAG exempts them) and takes WCAG's large-text allowance.

**It earned its place twice on the day it was written**, which is the answer to whether it was worth
having:

- the panel's new shortcut line, muted, sat at **1.66:1** on the accent fill of a primary button;
- `StartDialog`'s tile detail, also muted, went to **1.66:1** in Light and **1.00:1** in Dark the
  moment default buttons started taking the accent.

Both now inherit the button's own foreground, with the size difference alone carrying the hierarchy —
the same argument the palette already makes for `Chrome.Muted` collapsing to white in High Contrast.

It also caught **itself**: the first version set only `Application.RequestedThemeVariant` and was
silently measuring Light three times over, because a headless window that is never shown does not
pick the application's variant up. It now sets the variant per window and asserts it took.

**`NoChromeControlPaintsItselfWithALiteralColour`** is a source scan rather than a tree walk,
deliberately: the resolved tree cannot tell a brush that came from a token apart from an identical
brush somebody typed, and the point is to catch the typing. `Opacity` counts as a literal colour
there, because that is what it was being used for.

## 4. The twelve untokenised sites

All twelve moved: the four `SystemControlBackgroundChromeMediumLowBrush` binds (toolbar, status bar,
action panel, grid window), the `#FF6B6B6B` desk in **both** the places it was written, three
`Brushes.Firebrick`, two `Brushes.LightGray`, one `Brushes.Gray`, and the four `Opacity`
de-emphases.

`MainWindow.axaml`'s `CanvasScroller` and `Canvas/PageCanvasControl.cs` moved together, as the plan
required — they paint the same backdrop from two places and meet wherever the page does not fill the
scroller. Neither is the document: both fill the area *around* the sheet, which is chrome.
`PageCanvasControl` resolves the token through `TryFindResource` at draw time and repaints on
`ActualThemeVariantChanged`; **nothing in `Rendering` changed and no snapshot baseline was re-baked.**

## 5. The icons

`Theme/Icons.axaml` — 24 `StreamGeometry` resources, geometry only, no colour and no size. Every one
is a closed fill on a 0–24 grid, never a stroke. Several carry a counter with the `F0` even-odd fill
rule, which is still a fill.

`Theme/ActionIcons.cs` maps action ids and `IWidgetDefinition.IconKey` values to them.
**`EditorAction` gained no icon field:** `docs/M7-spec.md` already says the App layer maps keys to
glyphs, and a seventh positional field would have touched all 67 catalog entries in a diff where the
real change is invisible. **This closes M7's deferral of the icon set, which is void as of M16.**

The 24 are the eight toolbar commands, the six widget keys — exactly the six values
`IWidgetDefinition.IconKey` returns, so one dictionary closes M7 and serves the Insert group at once
— and ten primary or item-level actions.

**The map is deliberately partial and the 43 omissions are a design record, not a backlog.** Each is
listed with its reason in `ActionIcons.WithoutAnIcon`, and `IconTests.EveryActionEitherHasAnIconOrIs
ListedAsIconLess` requires the two sets to partition `ActionCatalog.All` exactly, so a new action
cannot arrive without an icon decision being made about it. `ActionGroup.Arrange` gets none because
four near-identical stacking arrows discriminate nothing; the clipboard gets none because it is
universally recognised and barely used on a layout canvas.

**One entry differs from the plan's arithmetic.** The plan said "ten primary or item-level actions"
and there are ten, but only nine of them are `IsPrimary`: `item.editList` is not, and it takes the
same pencil as `item.edit` because it sits directly beside it in the panel and reads as its twin
without one.

`IconText` owns the glyph, the fill binding and the wrap behaviour, and **cannot be constructed
without label text** — which is what makes "an icon never appears without its label" one assertion
rather than a thing reviewers must remember. Its fill binds to `Foreground`, not to a token: a
`DynamicResource` fill would paint a dark glyph on the navy primary button and would follow neither
hover, press nor disabled. It gained one parameter the plan did not name — `glyphSide` — because a
forward arrow on a "Next" button has to be on the side it points towards or it reads as "Back".

`↶ ↷ ◀ ▶ − +` are geometry now. `▸` stays: it means "this opens a further choice", which is a
different signal from an action icon and a platform convention. `▲ ▼` on the wizard's row-reorder
buttons also stay — they are not in the 24 and the map is allowed to be partial. **No toolbar button
was added or removed**, and every `AutomationProperties.Name` is unchanged.

**Menu items got no icons**, as a non-goal rather than deferred work: Fluent reserves an icon gutter
for a whole menu as soon as one sibling has an icon, so icons on the dozen commands that have them
would make the other thirty-five look broken.

## 6. The panel

The grouping the owner asked for **already existed** — `ActionCatalog.PanelGroups` orders groups per
selection and `DescribeGroup` names each in plain language. What was missing was any visual
difference between a heading and the buttons under it: 16pt SemiBold above 16pt buttons is not a
hierarchy. Headings now take the accent, a hairline rule and real space. **Static, never
collapsible:** a collapse control would make absence from the panel ambiguous for the first time, and
absence meaning "not about photos" is the rule M11 exists to enforce.

The wrapping fix, in order of effect: the shortcut moved to its own 14pt line, which removes 14–16
characters from most buttons and changes nothing a screen reader hears; the panel widened 320 → 360.
**No titles were shortened** — the plan allowed a few, sparingly, and none turned out to need it. The
longest is "Bring in birthdays from the address book" at 40 characters, which names both ends of an
operation whose whole difficulty is that people do not expect it to touch the address book.

`Editing.Tests.NoPanelTitleOutgrowsThePanel` sets the ceiling at 44 and says in its own summary that
it is a proxy: measuring shaped text in a unit test would couple the action catalog to the font
stack. It carries a second assertion that the longest title is *near* the ceiling, so the ceiling
cannot be raised until it stops meaning anything.

**`PanelFoldWidth` is now derived** — `ActionPanel.PanelWidth * 2.5` — instead of being a second
hard-coded number beside the first. It evaluates to 900, exactly what it was, so the fold behaviour
§11.9 of `docs/accessibility-test-script.md` tests by hand is unchanged.

**`EditorAction.IsPrimary` is finally read by something.** A primary offer takes the accent fill, a
taller minimum and a gold left bar — three signals, so colour is never the only one. The other half
of that field's old summary, "primary actions sort first in their group", is **withdrawn rather than
left lying**: declaration order already agrees with it, so a sort would be a near-no-op carrying real
risk of reordering a group a test depends on.

**`EveryPanelButtonStillReportsItsLabel` was not optional.** `ActionPanel.LabelOf` would have started
returning the empty string the moment the button content became an `IconText`, and that would have
failed *nothing*: of the four places the audit suite calls it, three use the result only to build a
failure message.

## 7. Hierarchy and spacing

The wizard had **one systematic bug, not sloppiness.** `wizard-officers-step.png` showed five
different left edges on one screen — x = 24, 62, 68, 24, 170 — because a `Width` or `MaxWidth` on a
child of a vertical `StackPanel`, under the default `HorizontalAlignment.Stretch`, makes Avalonia
centre that child. The arithmetic accounts for every edge exactly: 900px of window less the 24px
gutters is 852px of content, so the 780-wide header landed at 24 + (852−780)/2 = 60, the 760-wide
help text at 70 and the 560-wide input at 170, while the two uncapped children stayed at 24.

`HorizontalAlignment.Left` at each capped site fixes all of them. The three inputs took **`MinWidth`
in place of `Width`** rather than the plan's `MaxWidth`, and the difference is worth recording: with
`HorizontalAlignment.Left` a control is arranged at `min(available, desired)`, so a bare `MaxWidth`
would have shrunk every text box to its content. `MinWidth` keeps the 560 the design asked for and
lets a long value grow the box; a `MaxWidth` of 760 stops it running the width of a wide window.

The wizard footer was the one button bar that never got the shaded `Border` `WidgetGridWindow`
received on 2026-07-27. It has one now, with a top rule, and its `new Panel { Width = 40 }` spacer
became a `DockPanel`. The status bar gained a top border; it floated against the canvas before.

**Default-button emphasis is standardised in one place rather than thirteen.**
`Theme/Controls.axaml` carries a primary-button `ControlTheme` and a property-qualified
`Button[IsDefault=True]` selector that applies it. The `ControlTheme` restates `:pointerover` and
`:pressed`, because Fluent's own rules target the template's `ContentPresenter` and a plain
`Background` setter would have dropped the accent the instant a pointer touched it.

That file holds **one** bare element selector — `Style Selector="Window"`, for the icon — and says
why it is safe where the deleted overlay's were not: a `Window` is never nested inside another
control's template, so no `ComboBox` toggle or `ScrollBar` repeat button inherits it.

`Opacity` as a de-emphasis mechanism is gone everywhere it appeared. It multiplies against whatever
is behind it, so the result is unknown to the palette and invisible to the contrast gate.

## 8. The window icon

There was no `Icon=` anywhere in `src/` and no `AvaloniaResource` item in any `.csproj`:
`trestleboard.png` reached the Windows installer, the AppImage and the `.icns`, and never the running
application. It is **linked, not copied**, so there is still exactly one drawing of this icon in the
repository.

`IconTests.EveryWindowCarriesTheApplicationIcon` shows each window before checking, because that is
when a `Window` has the application's styles applied to it — and it is also the only moment an icon
means anything.

## 9. What is still open

- **The by-eye pass.** The gate settles contrast, tokens, icons and labels; it settles nothing about
  whether the result looks *designed*. PLAN.md §12 item 13 asks for Light, Dark and High Contrast at
  100% **and 200%**, confirming the toolbar, panel, status bar and canvas are distinguishable, focus
  is unmistakable, and the panel folds sensibly at both scales. **A machine cannot close this.**
- **The chrome-budget measurement at 200%** (§13, open since M11) now matters slightly more than it
  did: a 360px panel occupies 720px of a 1280px window at 200%, up from 640px.
- Everything else in PLAN.md §13 is unchanged by this milestone.
