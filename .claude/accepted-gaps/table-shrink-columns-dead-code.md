# `CssLayoutEngineTable.ShrinkColumnsToFitAvailableWidth` is untestable dead code

`CanReduceWidth(int columnIndex)` has its bounds check backwards -

```csharp
if (_columnWidths!.Length >= columnIndex || GetColumnMinWidths().Length >= columnIndex) return false;
```

- which is `true` for every in-range `columnIndex` (0 through `Length - 1`), so `CanReduceWidth(int)`
always returns `false`, and therefore so does the parameterless `CanReduceWidth()` that calls it in a
loop. `ShrinkColumnsToFitAvailableWidth`'s
`while (widthSum > GetAvailableTableWidth() && CanReduceWidth())` can consequently never enter its body
under any input - not "hard to trigger" but provably dead under the current `CanReduceWidth` behavior.
The bug is almost certainly meant to be `columnIndex >= _columnWidths.Length` (an out-of-range guard,
not an always-true one).

**Confirmed dangerous to fix in isolation.** During the issue #814 investigation, correcting the bounds
check alone was tried and reverted: `ShrinkColumnsToFitAvailableWidth` is called unconditionally as the
first step of `EnforceMaximumSize` - i.e. for every table's layout, not only when `max-width` is set - so
making it live this way regressed vertical-writing-mode column sizing (columns shrank when they should
have stayed at their specified/available width) and separately exposed a second bug: the inner
`while (!CanReduceWidth(curCol)) curCol++;` loop never wraps `curCol` back to 0, so once the outer guard
can be true, this inner loop can walk `curCol` past the end of `_columnWidths` and spin indefinitely
(observed as a test going from ~20ms to 10+ seconds before throwing, rather than completing).

Tracked as [issue #819](https://github.com/jhaygood86/PeachPDF/issues/819) - fixing this needs the bounds
check, the missing wraparound, *and* a real diagnosis of why activating this method shrinks
vertical-writing-mode table columns that shouldn't shrink (likely a physical-axis mismatch between
`GetAvailableTableWidth()`/`GetWidthSum()` and `_columnWidths` under `writing-mode: vertical-rl`), plus
full re-verification across `TableWritingModeIntegrationTests` and friends, since the method would newly
run for every table's layout, not just tables with `max-width`.

`ShrinkColumnsToFitAvailableWidth` stays marked `[ExcludeFromCodeCoverage]`, with this file linked in a
`<remarks>`, since no fix that's actually safe to ship has been found yet.

**`CssLayoutEngineTable.ClipColumnsToMaxWidth`** - previously bundled with this same coverage gap - is
no longer part of it: it's now confirmed live and correct on its own. `EnforceMaximumSize`'s guard
(`if (maxWidth < widthSum) ClipColumnsToMaxWidth(maxWidth);`, evaluated after columns are already at
their content minimum) doesn't go through `CanReduceWidth` at all, so it was reachable and working the
whole time - what masked it was two independent, now-understood things: unbreakable (`white-space:
nowrap`) content legitimately overflows `max-width` per CSS 2.1 §10.4/§17.5.2 (`EnforceMinimumSize`,
correctly, restores such a column past the clip immediately afterward - CSS spec, not a bug), and the
issue #814 root cause (font metrics computed at a device-scaled, artificially tiny size under a
non-default `PixelsPerPoint`) was making unbreakable test content measure far smaller than its true
width, so it happened to already fit under the fixture's `max-width` without needing real clipping at
all. `CssLayoutEngineTableTests.TableLayout_MaxWidthNarrowerThanExplicitWidth_RespectsMaxWidth` now uses
wrappable (not `nowrap`) content so it genuinely exercises the clip.
