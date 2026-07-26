# M9 — Templates, workflow, autosave, accessibility hardening

Status: derived from the LOCKED `PLAN.md` (§4, §6, §7, §11-M9) and the M1–M8 specs. This is the
milestone where the app stops being a toolkit and becomes something the committee can use, so most
of the decisions here are about what happens when things go wrong.

Acceptance (PLAN §11-M9): *kill the process mid-edit → relaunch offers recovery with thumbnail,
≤60s data loss; complete a full issue keyboard-only; NVDA reads every control on the main window.*

**Privacy (PLAN §0, HARD).** Templates are embedded in the repo, so every field in them is a
placeholder or an obvious prompt — never a real name, number or roster. This is stricter than the
rule for fixtures: a template is *shipped*, and whatever is in it will be printed by someone who
did not notice it was still there.

---

## 0. What this milestone cannot finish by itself

PLAN §11-M9 asks for the NVDA and VoiceOver manual test script **written and executed**. The script
is written here and is part of the deliverable. **Executing it needs a person at a machine with a
screen reader** — it is a manual script by definition, and no automated check substitutes for
hearing what NVDA actually says. The same is true of the "build Classic 414 by hand IN the app"
dogfooding instruction: the point of that instruction is to find the gaps that only appear when a
human drives the UI, and a programmatically-built template cannot find them.

So: the templates ship, the script ships, and both carry an explicit note that the human passes are
outstanding. They are recorded in §8 as the milestone's open items rather than quietly marked done.

---

## 1. Autosave and crash recovery

PLAN §4 fixes the shape: *every 60s + 5s-idle trigger, atomic write (temp+rename) of full `.tboard`
to `<AppData>/TrestleBoard/recovery/`; deleted on clean close; on startup, surviving file triggers a
large plain-language restore dialog with page-1 thumbnail. Also rotate last 5 autosaves as `.bak`
beside the user's file.*

### 1.1 The service

`RecoveryService` lives in `TrestleBoard.Editing` — headless, so the whole of it is testable without
a window, and so the timing rules are not tangled in a UI timer.

```csharp
public sealed class RecoveryService : IDisposable
{
    public RecoveryService(
        DocumentSession session,
        IRecoveryStore store,
        Func<byte[]?> thumbnailFactory,
        TimeProvider? time = null);

    public static readonly TimeSpan MaxInterval = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan IdleInterval = TimeSpan.FromSeconds(5);

    public bool HasUnsavedWork { get; }
    public int SaveCount { get; }

    /// <summary>Call on a tick; writes only when a rule says it is time.</summary>
    public bool Poll();

    /// <summary>Clean close: the recovery file is deleted, because there is nothing to recover.</summary>
    public void Complete();
}
```

- **Two rules, not one.** Idle: 5s after the last edit, so a user who pauses is protected almost
  immediately. Cap: 60s since the last write even if they never pause, so a continuous typist is
  never further than a minute from safety. The acceptance bound is "≤60s data loss"; the idle rule
  is what makes the real number much smaller.
- `TimeProvider` is injected, so the tests drive the clock rather than sleeping.
- **Nothing is written when nothing changed.** A document sitting untouched must not rewrite its
  recovery file every minute, or the file's timestamp stops meaning "when the work was done".

### 1.2 Atomicity

`IRecoveryStore.Write` takes the bytes and does temp-then-rename:

1. write `recovery/<id>.tboard.tmp`
2. flush to disk
3. `File.Move(tmp, final, overwrite: true)`

**Normative: never write the final path directly.** A crash halfway through a direct write leaves a
truncated zip, and a truncated recovery file is worse than none — it turns "your work is safe" into
"your work is safe, but the file is corrupt", which is the one thing a recovery feature must never
say. Rename is atomic on both NTFS and POSIX.

### 1.3 What is recovered

The recovery file is a complete `.tboard` — document, styles, assets — plus a **page-1 thumbnail**
in the container's existing `thumbnails/` area (it has been there since M2 and this is what it was
for). The restore dialog shows that thumbnail, because "is this the work I lost?" is a question
answered by looking, not by reading a filename.

Metadata carried alongside: the original file path (so recovery can offer to put it back where it
came from), and the timestamp of the last edit.

### 1.4 Clean close and the `.bak` rotation

- On clean close, `Complete()` deletes the recovery file. A recovery file surviving startup
  therefore *means* the app did not close cleanly — no heuristics, no "was it modified" guessing.
- Separately, saving over a user's file rotates the previous contents through `<name>.tboard.bak1`
  … `.bak5` beside it. That protects against the user's own mistakes, which PLAN §4 calls out
  explicitly and which is a different failure from a crash: an autosave cannot help someone who
  deliberately deleted a page and saved.

### 1.5 Startup

On launch, `RecoveryStore.FindRecoverable()` returns any surviving files. One or more → the restore
dialog, 20pt text, plain language, with the thumbnail and the time. Choosing "Put my work back"
opens the recovered document; choosing "Start fresh" deletes the file **after** confirming, because
that is the one irreversible button in the flow.

---

## 2. Templates

Three, embedded as `.tboard` resources in `TrestleBoard.Core`:

| Template | Shape |
|---|---|
| **Classic 414** | The existing look: cover banner + essay, officers page, birthdays sidebar. Recreates the structure the committee already knows. |
| **Simple 4-page** | Cover, two text pages, a back page with an announcement box. The fewest moving parts. |
| **6-page with photos** | Classic plus two photo-led spreads, for an issue with a lot to show. |

- Every text frame starts with a **prompt**, not sample prose: "Write the Worshipful Master's
  message here…". A prompt that survives to print is embarrassing; a fake brother's name that
  survives to print is a privacy incident.
- Widgets start EMPTY (M7 §8.3's rule, and the same reason).
- Templates are loaded through the ordinary `TboardContainer.Load`, so a template is just a
  newsletter — no second code path, and anything that can open a document can open a template.

## 3. Start-from-last-month

The single biggest monthly-effort win (PLAN §5/§7), so it gets the care:

1. Copy the document whole.
2. Bump `IssueMonth`/`IssueYear` by one, rolling the year at December.
3. **Recompute date-bound fields from `meetingRule`** using `Core.Text.MeetingRule.ResolveDate`,
   which has been in Core since M7 for exactly this. A CoverBanner storing `"1st Tuesday"` and
   `"July 7th"` gets a new `MeetingDateText` for the new month; the rule itself is untouched.
4. **Carry widget data forward unchanged.** Officers, committees and the district table barely
   change month to month; birthdays change every month but the user edits them, they do not retype
   them.
5. **Reset article text to prompts.** Last month's essay must not go out again under this month's
   date. Every story whose frames are plain text frames is replaced with its prompt.
6. Assets are carried forward, then unreferenced ones are pruned on save (M6's rule, unchanged).

**Normative: carry-forward never silently keeps prose.** The failure mode being designed against is
an issue going out with September's message under October's date, and the only safe default is to
clear it and make the user write.

## 4. Themes and scale

- Light (default), Dark, and a true High Contrast (7:1+), following the OS hint on first run and
  overridable in a Settings dialog that is reachable from the keyboard like everything else.
- UI scale 100–200%, independent of canvas zoom. The minimum UI font stays 16pt at 100% (PLAN §6),
  so 200% is 32pt — the setting is for people who need it, not a cosmetic preference.
- **Canvas rendering does not follow the UI theme.** The page is a piece of paper: it is white in
  Dark mode too, because the user is laying out something that will be printed. Chrome themes;
  paper does not.

## 5. Automation peers

The canvas is one control with a document inside it, so a screen reader sees nothing without help.
`PageCanvasAutomationPeer` exposes the page's blocks as children, each named in plain language:

| Block | Name |
|---|---|
| Text frame | `Text frame: <first few words>` |
| Photo | `Photo: <altText>` (the alt text M6 has demanded since insert) |
| Widget | `<widget display name>` — "Lodge officers", "Birthdays" |
| Shape | `Decoration` |

Names come from the same data the page prints, so they cannot drift. The peer tree is rebuilt when
the layout invalidates.

## 6. Keyboard audit

Two halves:

- **Automated**: a test asserts every menu item is reachable, that no command exists without a menu
  home, and that the shell's key handler covers every gesture the menus advertise. This is the half
  that will not rot.
- **Manual**: the script in §7, which is the only way to find out whether the *order* things are
  reached in makes sense.

## 7. The screen-reader script

`docs/accessibility-test-script.md` — a numbered manual script for NVDA (Windows) and VoiceOver
(macOS), covering: launch and start screen, opening a template, the main window's every control,
the canvas block tree, running the Officers wizard end to end, inserting a photo with its
description, exporting, and the recovery dialog. Each step records what the tester should HEAR, not
just what they should see, so a failure is unambiguous.

Linux AT-SPI is best-effort per PLAN §6 and the script says so.

## 8. Open items (deliberately not closed)

| Item | Why it is open | Who closes it |
|---|---|---|
| NVDA / VoiceOver script **executed** | Needs a human with a screen reader; no automated check substitutes for hearing it | The user, or a tester |
| "Classic 414" built **by hand in the app** | The instruction's value is finding UI gaps that only appear when a person drives it | The user |

## 9. Testing

| Project | Gates |
|---|---|
| `Core.Tests` | Carry-forward: month/year roll, date recompute from the meeting rule, widget data preserved, prose reset. Template resources load and are structurally sound. |
| `Editing.Tests` | `RecoveryService`: idle rule, 60s cap, no write when unchanged, atomic write order, clean close deletes, `.bak` rotation keeps five. |
| `App.HeadlessTests` | The recovery dialog appears for a surviving file and not otherwise; start screen tiles; a full issue driven keyboard-only; the canvas peer names every block. |
| `Rendering.SnapshotTests` | Each template's page 1, so a template cannot rot unnoticed. |

## 10. Deferrals

| Item | Why |
|---|---|
| Cloud or network backup | v1 is a desktop app for one committee; PLAN §1 non-goal. |
| Recovering more than one document at once | The app opens one newsletter at a time. |
| Per-widget carry-forward choices ("keep officers, clear birthdays") | A checkbox list is a worse default than "carry data, clear prose"; revisit if the committee asks. |
| Theme authoring | Three themes, not a theme editor. |
| Linux screen-reader parity | PLAN §6 calls AT-SPI best-effort; the script records what was observed rather than promising a level. |
