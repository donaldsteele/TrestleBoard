# TrestleBoard

A purpose-built desktop editor for producing a Masonic lodge's monthly trestle board
newsletter — edit text, tables, and photos on a free-form page canvas, fill recurring
sections (officers, birthdays, committees, district calendar) with simple step-by-step
wizards, and export a distribution-ready PDF.

Built for Indian Land Lodge 414's trestle board committee, with accessibility for
elderly users as a first-class requirement.

- **Platforms:** Windows, Linux, macOS (.NET 10 + Avalonia)
- **Rendering:** custom SkiaSharp/HarfBuzzSharp layout engine shared by the on-screen
  editor and PDF export — what you see is exactly what prints
- **File format:** `.tboard` (zip container; originals of every photo kept losslessly)

## Installing

Downloads for Windows, macOS and Linux are on the
[releases page](https://github.com/donaldsteele/TrestleBoard/releases).
**[docs/INSTALL.md](docs/INSTALL.md)** walks through it in plain language, including the
SmartScreen and Gatekeeper warnings that appear because the app is not code-signed. Installed
copies update themselves from that same releases page.

## Building

Requires the .NET 10 SDK (see `global.json`).

```
dotnet build TrestleBoard.slnx
dotnet test TrestleBoard.slnx
dotnet run --project src/TrestleBoard.App
```

See `PLAN.md` for the full architecture and milestone plan.
