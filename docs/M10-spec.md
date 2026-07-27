# M10 — Packaging & release

Status: derived from the LOCKED `PLAN.md` (§1, §9 packaging note, §11-M10). This is the milestone
where the work leaves the developer's machine, so almost every decision here is about what happens
on a computer nobody involved in the build can see.

Acceptance (PLAN §11-M10): *on fresh Windows and macOS machines: download → install → open template
→ export PDF following only the written instructions; pushing a new tag produces an update an
installed copy picks up automatically.*

**Privacy (PLAN §0, HARD).** Nothing in this milestone touches document content. The one place real
data could leak is a crash/telemetry channel — there is none, and none is added.

---

## 0. What this milestone cannot finish by itself

Two parts of the acceptance criterion need hardware and a person:

1. **The clean-machine install test.** "Fresh Windows and macOS machines" means exactly that. The
   Windows package is built and verified locally (`vpk pack` runs clean, `VelopackApp.Run()` is
   verified by the packer, a 56 MB Setup.exe is produced); the macOS and Linux packages are built
   by the release workflow on GitHub's runners and have not been *installed* by anyone yet.
2. **The install screenshots.** `docs/INSTALL.md` asks for pictures of the SmartScreen and
   Gatekeeper warnings. Photographing them needs the same fresh machines. The document quotes the
   on-screen wording exactly and says plainly that the pictures are outstanding, rather than
   shipping text that pretends they are there.

Both are recorded in §6 as open items. Everything else in the milestone ships.

---

## 1. Packaging

Velopack 1.2.0 (`Directory.Packages.props`), and the `vpk` CLI pinned to the same version — the CLI
writes the release metadata the library reads, so they move together.

Four RIDs, each its own Velopack **channel** named for the RID (`win-x64`, `linux-x64`, `osx-x64`,
`osx-arm64`). Channels are what let two macOS architectures live in one GitHub release without
offering an Intel build to an Apple-silicon Mac: an installed copy records the channel it came from
and only ever looks there again.

Publish is **self-contained but not single-file**, which is a deliberate deviation from the wording
in PLAN §9. Velopack packs a *folder* and diffs consecutive folders to build delta updates; a single
packed executable defeats the diff and every update becomes a full ~55 MB download. Trimming stays
off, as the plan says.

Icons live in `assets-src/icons/` (`.ico` for the Windows installer, `.png` for the AppImage) and are
drawn with SkiaSharp rather than sourced, so there is no third-party artwork to license. macOS gets
an `.icns` built during the release run by `sips`/`iconutil`, which only exist on a Mac.

## 2. Updating

`Updates/UpdateCoordinator.cs` is the whole policy, and the policy is: **an update may never
interrupt the work.**

- The check runs once, in the background, shortly after startup, against
  `https://github.com/donaldsteele/TrestleBoard` releases. Pre-releases are ignored.
- If there is something newer it is downloaded quietly.
- It is **applied when the user closes the app** (`WaitExitThenApplyUpdates(…, restart: false)`),
  after the close handler has already written any unsaved work. Nobody's window disappears
  mid-sentence, and nobody is asked to decide anything.
- A background check that finds nothing says nothing. **Help → Check for an update** always answers,
  because a button that appears to do nothing is worse than no button.
- Every failure — no network, GitHub down, a half-written download — is a status-bar line at most,
  and only when the user asked. The app keeps working.
- The coordinator talks to `IUpdateChannel`, so the shell wiring is tested against a fake and no
  test run ever touches the network. `VelopackUpdateChannel` is the one real implementation.

`UpdateState.NotInstalled` covers running from a build output or a portable copy: the app says so
rather than pretending to check.

## 3. Opening a `.tboard` by double-click

Three platforms, three unrelated mechanisms, one landing point (`MainWindow.OpenDocumentFromPath`):

| Platform | How the association is made | How the path arrives |
|---|---|---|
| Windows | `HKCU\Software\Classes` — the `.tboard` key, the `TrestleBoard.Newsletter` ProgId, `DefaultIcon`, `shell\open\command`. Per-user, so no elevation prompt ever appears. Written from Velopack's after-install/after-update hooks, removed from the before-uninstall hook. | argv |
| Linux | `~/.local/share/mime/packages/trestleboard.xml` + `~/.local/share/applications/trestleboard.desktop`, then `update-mime-database`/`update-desktop-database` best-effort. Written on first run (Linux has no Velopack install hooks). | argv |
| macOS | `CFBundleDocumentTypes` + `UTExportedTypeDeclarations` in `build/macos/Info.plist`, baked into the bundle before packing. Launch Services honours nothing else. | Avalonia's `IActivatableLifetime` file-activation event — macOS does not pass the document on the command line |

The Windows open command quotes both the executable and `%1`. Lodge machines keep documents under
"My Documents" and the app under "Program Files", and an unquoted command line loses everything
after the first space in either — which reaches us as "TrestleBoard cannot find C:\Users\Don\My".

`StartupOptions.Parse` takes the first `.tboard` argument and ignores everything else, because
Velopack's own switches come through the same argv and an argument the app does not understand must
never stop it from starting. A `.tboard` on the command line skips both the recovery offer and the
start screen: double-clicking a file is an instruction, not a suggestion. A path that is missing or
is not a newsletter becomes a status-bar sentence and the normal start flow — an association can
outlive the file it points at (a USB stick that is no longer plugged in).

Since Velopack builds the macOS bundle itself but cannot add document types to its Info.plist,
`build/macos/make-app-bundle.sh` assembles the `.app` and hands it to `vpk pack` complete.

## 4. Releasing

`.github/workflows/release.yml`, triggered by a `v*` tag (or by hand with an explicit version).

1. **verify** — the full test suite on ubuntu. A tag push does not run `ci.yml`, and shipping
   something that failed its own tests to people who cannot easily roll back is not a risk worth
   taking.
2. **pack** — one matrix leg per RID, `max-parallel: 1`. Publish → (macOS: build the bundle) →
   `vpk pack` → upload the packages as build artifacts → `vpk upload github --merge --publish`.

`--merge` is what puts all four RIDs in one release; `max-parallel: 1` is what stops the four merges
racing each other. The build artifacts are kept even on a successful run so a half-published release
can be finished by hand.

The version comes from the tag (`v1.2.3` → `1.2.3`) and is passed to `dotnet publish` as
`-p:Version=`. Nothing in the repository has to be edited to cut a release.

## 5. Instructions for the committee

`docs/INSTALL.md` — plain language, one instruction per line, exact quotes of every warning the user
will see, and the "why" stated once (no signing certificate) without dwelling on it. It ends with the
maintainer's two-line release procedure, which belongs in the same file as the thing it produces.

`README.md` links to it.

## 6. Open items

- [ ] Clean-machine install on fresh Windows and macOS, following only `docs/INSTALL.md` (PLAN
      §11-M10 acceptance, §12 item 6). Needs machines and a person.
- [ ] Screenshots of the SmartScreen and Gatekeeper warnings for `docs/INSTALL.md`.
- [ ] End-to-end auto-update round trip: install `v0.1.0`, publish `v0.1.1`, confirm an installed
      copy takes it. Needs the first two real tags.
- [ ] The macOS and Linux asset filenames quoted in `docs/INSTALL.md`
      (`TrestleBoard-osx-arm64-Setup.pkg`, `TrestleBoard-linux-x64.AppImage`) follow the Windows
      naming Velopack produced locally; confirm them against the first real release and correct the
      document if they differ.
