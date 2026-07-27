# Application icons

`trestleboard.png` (512×512) and `trestleboard.ico` (a single 256×256 PNG-compressed entry) are the
icons the installers and the AppImage use (docs/M10-spec.md §1). They are drawn, not photographed
or downloaded: a sheet of paper with a drafting square over it — a trestle board is a drawing board,
and the square is the lodge's own emblem of upright work. No third-party artwork, so nothing to
license and nothing to attribute.

Both are generated with SkiaSharp (already in the stack) so they can be redrawn rather than
hand-edited. To regenerate, run this against SkiaSharp 3.119.4 with the output path as `args[0]` and
the pixel size as `args[1]` — a `.ico` extension writes the ICO container, anything else a PNG:

```csharp
int Size = int.Parse(args[1]);
using var surface = SKSurface.Create(new SKImageInfo(Size, Size, SKColorType.Rgba8888, SKAlphaType.Premul));
SKCanvas c = surface.Canvas;
c.Clear(SKColors.Transparent);
float k = Size / 512f;
c.Scale(k, k);

var bg = new SKPaint { Color = new SKColor(0x1E, 0x33, 0x5C), IsAntialias = true };
c.DrawRoundRect(new SKRoundRect(new SKRect(16, 16, 512 - 16, 512 - 16), 72, 72), bg);

var gold = new SKPaint
{
    Color = new SKColor(0xC8, 0xA2, 0x4B),
    IsAntialias = true,
    Style = SKPaintStyle.Stroke,
    StrokeWidth = 26,
    StrokeCap = SKStrokeCap.Round,
    StrokeJoin = SKStrokeJoin.Round,
};

var sheet = new SKPaint { Color = SKColors.White, IsAntialias = true };
c.DrawRoundRect(new SKRoundRect(new SKRect(128, 108, 384, 404), 12, 12), sheet);

var rule = new SKPaint { Color = new SKColor(0x9A, 0xA5, 0xB8), IsAntialias = true, StrokeWidth = 14, StrokeCap = SKStrokeCap.Round };
for (int i = 0; i < 4; i++)
{
    float y = 168 + (i * 46);
    c.DrawLine(168, y, i == 3 ? 292 : 344, y, rule);
}

using var square = new SKPath();
square.MoveTo(150, 300);
square.LineTo(310, 300);
square.LineTo(310, 140);
c.DrawPath(square, gold);
```

The ICO container is 22 bytes of header followed by the 256×256 PNG bytes: `0,0` (reserved),
`1,0` (type = icon), `1,0` (one image), then the directory entry `0,0,0,0` (256×256, no palette),
`1,0` (planes), `32,0` (bpp), the PNG length, and offset `22`.

macOS gets an `.icns` built from `trestleboard.png` by `build/macos/make-app-bundle.sh` using the
`sips`/`iconutil` tools that only exist on a Mac — which is why the `.icns` is not committed here.
