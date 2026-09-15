# A malformed/corrupted simple glyph's `endPtsOfContours` could overrun `xs`/`ys`

## What was investigated

macOS CI on PR #1094's run hit an unhandled `IndexOutOfRangeException` inside
`GlyphOutlineDecoder.DecodeSimple` (`src/PeachPDF/Fonts/OpenType/GlyphOutlineDecoder.cs`), reached via
`GraphicsAdapter.MeasureInkCrossings` → `OpenTypeDescriptor.TryGetGlyphOutline` while decoding the letter
`g` for `GraphicsAdapterInkCrossingsTests.ADescender_CrossesABandBelowTheBaseline`. Ubuntu and Windows
passed in the same run; a rerun of the identical macOS job on identical code passed cleanly; `main`'s own
CI has stayed green. Single, non-reproduced failure.

The obvious suspect - the exact class of pre-existing shared-read-cursor data race documented in
[[2026-09-13-opentypefontface-shared-cursor-data-race]] - was ruled out by ancestry: that fix (merge
commit `8152c8b2`, PR #1026) is a confirmed ancestor of the commit whose CI run crashed, so
`GlyphOutlineDecoder.TryGetGlyphOutline`'s `lock (face.SyncRoot)` was already in place. Notably, that
same fix's own writeup left one residual, unreproduced failure open in this exact test class under
*forced 64-thread* stress (`TryGetGlyphOutline_CompositeGlyph_AddsAccentContoursAboveTheBase`) - so a
subtler residual race in this file was already flagged as a possibility worth revisiting, not ruled out.

A full audit of the call chain (`FontFactory`'s static caches, `XFontSource.GetOrCreateFrom`,
`OpenTypeFontface.CetOrCreateFrom`, the per-instance vs. global font-resolution split for
`@font-face`/`AddFont`-registered families) found every other shared, process-wide cache correctly
guarded by `Lock.EnterFontFactory()`/`ExitFontFactory()` or `FontFactory.CacheFontSource`'s check-under-
lock pattern - no second unlocked shared-cursor or unlocked-Dictionary path was found feeding this
specific call path (`Fixture.CreateAsync` → `adapter.GetFont` → `MeasureInkCrossings` for a custom,
per-instance-cached test font). No reproduction was achieved by static analysis alone, and stress-testing
with forced high parallelism was deliberately not attempted on this machine - see
[[project_windows_crash_root_cause]] (auto-memory): this exact machine has previously crashed and
silently corrupted on-disk build output under sustained `dotnet test` load, so repeating that here to
chase a macOS-only, non-reproduced failure was not a reasonable trade.

## What was found and fixed instead

While auditing `DecodeSimple` byte-for-byte against the exact reported exception shape, a genuine,
independent bounds bug surfaced: `numPoints` is sized from **only the last entry** of
`endPtsOfContours` (`numPoints = endPtsOfContours[numberOfContours - 1] + 1`), but the per-contour split
loop trusted every entry to be monotonically increasing without checking:

```csharp
for (int c = 0; c < numberOfContours; c++)
{
    int contourEnd = endPtsOfContours[c];
    for (; pointIndex <= contourEnd; pointIndex++)   // no upper bound from numPoints
        contourPoints.Add(new RawPoint(xs[pointIndex], ys[pointIndex], ...));
}
```

If any **earlier** contour's end-point value exceeds the last one - a malformed glyph, or bytes
corrupted by any means (disk I/O flake, a CI runner hiccup, or the exact kind of transient corruption
this developer's own machine has independently exhibited under load) - `pointIndex` walks past
`xs`/`ys`'s length, throwing `IndexOutOfRangeException` from exactly the line the crash report named.
This reproduces the **exact reported stack trace** (`DecodeSimple` → `DecodeInto` → `TryGetGlyphOutline`)
when the guard is reverted, confirmed by temporarily removing it and re-running the new regression test.

This is offered as a plausible, well-evidenced explanation of the observed crash shape - not a proven
identification of what corrupted this particular CI run's bytes - since the true trigger on that one
macOS run was never captured. It is a legitimate hardening either way: a font decoder should not crash
on malformed input regardless of why the input was malformed, and the existing contract
(`TryGetGlyphOutline` already returns `false`/empty for other malformed cases - absent tables,
out-of-range index, empty glyph) did not previously cover this one.

## The fix

Capped the inner loop at `numPoints` in addition to `contourEnd`:

```csharp
for (; pointIndex <= contourEnd && pointIndex < numPoints; pointIndex++)
```

For any well-formed font, `endPtsOfContours` is monotonically increasing by construction and the last
entry always equals `numPoints - 1`, so this is a no-op on valid data - confirmed by the full test suite
passing unchanged. On malformed data it discards the contours after the corrupted point cleanly (bounded
garbage, not a crash) rather than reading out of bounds.

## Evidence

- New regression test `GlyphOutlineDecoderTests.TryGetGlyphOutline_NonMonotonicEndPointsOfContours_DoesNotOverrunPointArrays`
  patches a real bundled font's `'o'` glyph bytes in place (`endPtsOfContours[0]` set to `0xFFFF`,
  exceeding `endPtsOfContours[1]`) using the same byte-patching technique as
  `GlyphDataTableTests.CompleteGlyphClosure_IncludesNestedCompositeComponents`. Reverting the fix
  reproduces the reported `IndexOutOfRangeException` at the same three-frame call chain; with the fix,
  the corrupted contour is discarded (asserted: exactly one contour survives) and nothing throws.
- Full `PeachPDF.Tests` suite, `--framework net8.0`: 11678 passed, 0 failed, 9 skipped (unrelated,
  platform-gated MIME-type tests) - no regression from the added bound.
- `diff-cover` against the changed lines: 100% coverage on `glyphoutlinedecoder.cs`.

## What this does not close

If the macOS crash recurs, this fix will only help if the trigger really was out-of-bounds contour data;
if it recurs with the guard in place, that's evidence the true cause is something else entirely (e.g. a
genuine timing-dependent race this investigation didn't turn up, or CI infrastructure flakiness) and is
worth reopening as its own investigation rather than assuming this fix was insufficient.
