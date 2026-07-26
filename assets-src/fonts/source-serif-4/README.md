# Source Serif 4 — bundled document font (body text)

- Version: **4.005** (release tag `4.005R`)
- Source: https://github.com/adobe-fonts/source-serif/releases/tag/4.005R (`source-serif-4.005_Desktop.zip`, `TTF/`)
- License: SIL Open Font License 1.1 — see `LICENSE.md` in this directory.

M1 subset: Regular, Bold, Italic (`It`), BoldItalic (`BoldIt`) static TTFs. Static
instances (not the variable font) are used deliberately: fixed outlines and metrics keep
shaping and rendering byte-deterministic across OSes (PLAN.md §3 determinism requirement).

These files are embedded as resources in the rendering/layout stack and loaded via
`SKTypeface.FromStream` — never from system font directories. Do not re-download a
different version without re-generating every snapshot baseline.
