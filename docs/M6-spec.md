# TrestleBoard M6 — Imaging: Implementation Spec

Status: derived from the LOCKED `PLAN.md` (§2 `ImageRecipe`, §9 image-pipeline details, §11-M6).
Compact by design — §9 already specifies both algorithms; this pins down order, contracts, and the
one new dependency edge.

Acceptance (PLAN §11-M6): drop a fixture photo → auto-cropped to the frame aspect; auto-levels
fixtures pass histogram assertions; **originals byte-identical in the container**; every edit
undoable.

Repo facts this builds on:

- `ImageRecipe` already exists in Core: `CropNormalized` (RectPt in [0,1] source coordinates),
  `RotationSteps` (0–3 clockwise), `Brightness`, `Contrast`, `Saturation`, `AutoLevels`, plus an
  extension bag. `SetImageRecipeCommand` already applies/reverts it.
- `ImageFrame` carries `AssetRef`, `Recipe`, `Fit` (Cover/Contain/Stretch), `Caption`, `AltText`.
- `TboardContainer` writes assets with `CompressionLevel.NoCompression` in ordinal order — asset
  bytes already survive a save/load round trip unchanged.
- `DocumentRenderSource` currently ignores the recipe entirely (M3 aspect-fill only).
- `TrestleBoard.Imaging` is an empty stub project with no references.

---

## 0. Dependency edge (the one structural decision)

PLAN §9 lists `TrestleBoard.Imaging … Deps: SkiaSharp` and CLAUDE.md summarises the flow as
"Imaging standalone". M6 adds exactly one edge: **`Rendering → Imaging`**. Painting a page has to
apply the recipe, and the alternative (duplicating the pipeline inside Rendering) would put two
implementations of auto-levels in the repo.

Imaging itself keeps its stated dependencies: **SkiaSharp only, never Core**. Recipes cross the
boundary as a plain `ImageRecipeSpec` record that Rendering maps from `Core.Model.ImageRecipe`, so
Imaging stays a leaf usable by a future CLI or test harness with no document model attached.

---

## 1. Decode (EXIF-aware)

`ImageDecoder.Decode(ReadOnlySpan<byte>) → DecodedImage(SKBitmap Bitmap, SKEncodedOrigin Origin, SKSizeI SourcePixelSize)`

- Decode through `SKCodec` so `codec.EncodedOrigin` is available, then **bake the orientation into
  the pixels** before anything else touches them. Every later stage — crop, energy map, levels —
  therefore works in upright coordinates, and `CropNormalized` means what the user saw.
- All eight EXIF origins are handled (the four rotations and their mirrored twins).
- Decode failures return null rather than throwing: a corrupt asset must degrade to the grey
  placeholder, never crash the editor.

## 2. Recipe pipeline (fixed order)

`ImagePipeline.Render(DecodedImage, ImageRecipeSpec, SKSizeI targetPx) → SKImage`

Order is fixed and normative — the algorithms are not commutative:

1. **Crop** — `CropNormalized` in upright source coordinates, clamped to the image, null = full.
2. **Rotate** — `RotationSteps × 90°` clockwise (user rotation, on top of the EXIF baseline).
3. **Auto-levels** — when `AutoLevels` is set (§3), computed on the *cropped, rotated* pixels so
   the stretch reflects what is actually shown.
4. **Colour sliders** — brightness / contrast / saturation as one `SKColorFilter` matrix chain,
   each nominally in [-1, 1] with 0 = untouched.
5. **Resample** to `targetPx` with `SKCubicResampler.Mitchell` (the M3 sampling choice).

Non-destructive is absolute: the pipeline never writes back to the asset bytes. The container keeps
the original encoded file exactly as it arrived (acceptance criterion 3).

## 3. Auto-levels (PLAN §9)

Per-channel **0.5% percentile clip + linear stretch**, with a **luminance-only mode** that computes
one stretch from the luma histogram and applies it to all three channels — the tint-safe option for
photographs that are legitimately warm (candlelight lodge rooms).

- Histograms are 256-bin, built on the post-crop pixels.
- A channel whose clipped range collapses (`high <= low`) is passed through unchanged instead of
  amplified — flat/monochrome fixtures must not blow up.
- Default for "Fix photo" is **luminance-only**; per-channel is available behind the Adjust panel.

## 4. Auto-crop (PLAN §9)

`AutoCrop.Propose(DecodedImage, float targetAspect) → RectPt` (normalized).

1. Downscale to a 256px long edge (analysis only — never the output).
2. **Sobel energy map** on luma.
3. **Skin-tone bonus**: lodge photos are mostly people, so pixels inside a normalized RGB
   skin-tone envelope add a weighted bonus to the energy map.
4. Slide every target-aspect window over the image on a coarse grid; score = energy inside the
   window **minus a border-cut penalty** for energy sliced off at the window edge (which is what
   makes it avoid cutting through faces).
5. Return the best window as normalized coordinates; the user adjusts it in the preview.

Determinism: integer grid, fixed step, ties broken toward the centre — the same input always
proposes the same crop, on every OS.

## 5. Cache

`RecipeCache` keyed by `(assetRef, recipeHash, targetPx)` where `recipeHash` is a stable FNV-1a over
the recipe fields (not `GetHashCode`, which is not stable across runs). Bounded LRU (default 32
entries) disposing evicted `SKImage`s. A slider drag therefore re-renders once per distinct value
and repaints from cache afterwards.

## 6. Commands and asset lifecycle

- Every photo edit is a `SetImageRecipeCommand` — "Fix photo" is one command carrying the proposed
  crop *and* the auto-levels flag, so one Undo restores the original look.
- Inserting a photo is a `CompositeCommand` "Insert photo" = `AddBlockCommand(ImageFrame)`, with the
  bytes registered in the package **and** in the render source before the command runs.
- Asset bytes are not part of the document model, so they are not undone; an asset that ends up
  unreferenced is pruned when the container is written (save arrives with M9). Documented here so
  the rule is not re-invented later.
- Alt text is prompted at insert (PLAN §6 — screen-reader users must not meet an unlabelled photo)
  and stored on the block; it is part of the same insert composite.

## 7. UI (PLAN §9: one big button)

- **"Insert photo…"** dialog beside drag-and-drop — the dialog is the accessible path and comes
  first (PLAN §6: drag-drop is never the only route).
- **"Fix photo"** is the primary control: auto-crop to the frame's aspect + luminance auto-levels,
  one click, one undo step, plain-language label.
- **"Adjust…"** opens three large sliders (brightness, contrast, saturation) plus the crop preview;
  changes coalesce per slider drag into one undo step.
- Every action is also an Object-menu item with a shortcut, per the M5 keyboard-parity rule.

## 8. Deferrals

Rotation UI beyond 90° steps, red-eye, sharpening, colour management/ICC, HEIC (SkiaSharp does not
decode it on all three platforms), multi-image batch fix, and cropping by dragging inside the frame
(the preview panel is the M6 path).
