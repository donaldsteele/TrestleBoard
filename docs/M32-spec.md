# M32 — The ligature caret

**Delivered 2026-08-07.** The last confirmed Major from §14.2, and the one M31's spec called "the
riskiest thing left" — it moves caret and selection geometry, so it was held back for a milestone of
its own with snapshot review rather than folded into a sweep.

---

## 1. A cluster is not a character

`TextLayoutEngine.BuildSegment` built each positioned run's source span as:

```csharp
new SourceSpan(para.StoryId, paragraphIndex, clusters[0], clusters[^1] + 1)
```

`clusters[^1]` is the cluster index of the last **glyph** — and a cluster is a range of source
characters, not one of them.

Shaping `"affix coffin fi"` through the bundled body font, with the standard ligatures the engine
enables by default, gives clusters `0,1,4,5,6,7,8,11,12,13`:

| Cluster | Characters | Why |
| --- | --- | --- |
| 1 | `ffi` (1–3) | one ligature glyph |
| 8 | `ffi` (8–10) | one ligature glyph |
| 13 | `fi` (13–14) | one ligature glyph |

So `clusters[^1] + 1` produced **14** for a fifteen-character paragraph. The run's span stopped
inside the final ligature.

## 2. What that broke

Everything built on the span inherited the error:

- `StoryTextGeometry.SegmentSpan` reports a segment ending one or two characters short, so a
  selection dragged to the end of a line does not cover the last ligature.
- `XToOffset` uses `target.Source.EndChar` as the last cluster's exclusive end, so clicking the
  **right half of a trailing ligature** resolves to an offset *inside* it, with Leading affinity —
  a caret position that is not a grapheme boundary.

Every one of those is silent. Nothing throws; the caret is simply in the wrong place, in exactly the
words a serif body font ligates most — which in English is most words containing "fi" or "ffi".

## 3. The fix

`ShapedRun.ClusterEnd(int cluster)` answers where a cluster actually ends: the next cluster boundary
in the run, or the run's own text end when there is none.

It is asked of the whole **shaped** run rather than of the glyphs placed on one line, deliberately —
the boundary may be a glyph that landed on the following line, and that is still where this cluster
stops.

No snapshot baseline moved. Glyph positions were always right; only the source spans describing them
were wrong, and nothing on the rendering path reads those.

---

## 4. How the test was got right

The first version of this test used text ending in `"coffin"` and **passed against the unfixed
engine** — because the last cluster there is the plain `n`, for which `+1` is correct. The bug only
appears when a run *ends* in a ligature.

Finding that took a throwaway probe that dumped the actual cluster arrays, which is what produced
the table in §1. The test now ends in `fi`, and fails without the fix.

That is the third time in M24–M32 that a regression test passed against the unfixed code. It is the
single most useful habit from this whole run of milestones: **write the test, then break the fix and
watch it fail.** A test that has never been seen to fail is a comment.

## 5. What guards it

`StoryTextGeometryTests.ARunEndingInALigatureSpansTheWholeOfIt` — the span reaches the end of the
paragraph, no span claims more than the paragraph holds, and every span is non-empty. Confirmed to
fail against the unfixed engine.

Suite after M32: **1172 passing, 12 skipped**.
