# Installing TrestleBoard

This page is written for the person who is going to use TrestleBoard, not for a programmer. If you
get stuck at any step, stop and ask — nothing here can break your computer.

TrestleBoard is free and is not signed with a paid certificate, so **Windows and macOS will both
warn you the first time you open it**. The warning is about the certificate, not about the program.
The steps below tell you exactly what to click.

> **About the pictures.** The screenshots that belong beside each warning have not been taken yet —
> they need a real fresh Windows machine and a real Mac to photograph. Until they are, the words you
> will see on screen are quoted exactly, so you can match them letter for letter.

---

## Windows

1. Go to **https://github.com/donaldsteele/TrestleBoard/releases** and open the newest release at
   the top of the page.
2. Under **Assets**, click **TrestleBoard-win-x64-Setup.exe**. It will download — about 55 MB, a
   minute or two on most connections.
3. Open the downloaded file (your browser usually shows it at the bottom of the window, or it is in
   your **Downloads** folder).
4. **Windows will show a blue box that says "Windows protected your PC".** This is SmartScreen. It
   appears because nobody has paid Microsoft for a signing certificate.
   - Click the small underlined words **More info**.
   - The box grows and shows a new button: **Run anyway**. Click it.
   - If you do not see "More info", click the box once first — it appears after the box has focus.
5. TrestleBoard installs itself and opens. There is nothing to choose; there is no "next, next,
   finish".
6. You now have **TrestleBoard** on your Desktop and in the Start menu.

**Updates.** Nothing to do. When a new version is published, TrestleBoard downloads it quietly while
you work and installs it when you close the app. The next time you open it, it is the new version.
You can check at any time with **Help → Check for an update**.

**Your newsletters.** Files ending in `.tboard` are yours. Double-click one and it opens in
TrestleBoard.

---

## macOS

> **The 0.1.0 release has no Mac files on it.** A packaging fault stopped the two Mac builds from
> being published; it is fixed, but the fix only takes effect the next time a release is made. If the
> newest release has no `.pkg` file under **Assets**, there is nothing to download yet — this is not
> something you are doing wrong.

1. Go to **https://github.com/donaldsteele/TrestleBoard/releases** and open the newest release.
2. Under **Assets**, download the file that matches your Mac:
   - **TrestleBoard-osx-arm64-Setup.pkg** — Macs from late 2020 onward (Apple M1, M2, M3, M4).
   - **TrestleBoard-osx-x64-Setup.pkg** — older Intel Macs.
   - Not sure? Click the  menu in the top-left corner → **About This Mac**. If it says **Chip:
     Apple M-something**, use arm64. If it says **Processor: Intel**, use x64.
3. Open the downloaded `.pkg` file.
4. **macOS will say "TrestleBoard.pkg cannot be opened because it is from an unidentified
   developer"** (or, on newer versions, "Apple could not verify TrestleBoard is free of malware").
   This is Gatekeeper, and it is about the certificate, not the program.
   - Click **OK** or **Done** to close that message.
   - Open **System Settings** →**Privacy & Security**.
   - Scroll down to the **Security** section. There is a line about TrestleBoard being blocked, and
     beside it a button: **Open Anyway**. Click it, and confirm with your password or Touch ID.
   - Alternatively, in **Finder**, hold **Control** and click the downloaded file, choose **Open**
     from the menu that appears, and then click **Open** in the box that follows. That
     right-click-then-Open route is the one Apple documents for unsigned software.
5. The installer runs and puts **TrestleBoard** in your **Applications** folder.

**Updates.** The same as on Windows: quiet download, installed when you close the app.

**A note on the warning.** It will appear once per new version you install. That is expected. Signing
the app costs an Apple developer subscription the lodge has not bought — the decision is recorded in
`PLAN.md`.

---

## Linux

1. Go to **https://github.com/donaldsteele/TrestleBoard/releases** and open the newest release.
2. Under **Assets**, download **TrestleBoard-linux-x64.AppImage**.
3. Make it runnable, either through your file manager (Properties → Permissions → "Allow executing
   file as program") or in a terminal:

   ```
   chmod +x TrestleBoard-linux-x64.AppImage
   ```

4. Run it. There is no separate install step; the AppImage is the whole application.

The first run also registers `.tboard` files with your desktop, so double-clicking a newsletter
opens TrestleBoard.

---

## If something goes wrong

- **"Windows cannot access the specified device, path, or file."** Your antivirus quarantined the
  download. Get the file back from the antivirus program's quarantine list, or download it again.
- **The app opens and immediately closes.** Note anything on the screen and open an issue at
  https://github.com/donaldsteele/TrestleBoard/issues — include which system you are on.
- **You lost work in a crash.** Open TrestleBoard again. It offers you back what you were working
  on, with a picture of the first page, and loses at most the last minute (PLAN.md §4).
- **You want to go back to an older version.** Every release stays on the releases page; download an
  older Setup and run it.

---

## For maintainers: cutting a release

`.github/workflows/release.yml` does everything. Tag a commit and push the tag:

```
git tag v1.2.3
git push origin v1.2.3
```

The workflow runs the full test suite, publishes self-contained builds for `win-x64`, `linux-x64`,
`osx-x64` and `osx-arm64`, packs each with Velopack, and merges all four into the GitHub release for
that tag. Installed copies of the app poll that release feed and update themselves.

Versions come from the tag (`v1.2.3` → `1.2.3`); nothing in the repository needs editing first.
`workflow_dispatch` takes a version by hand for a re-run.
