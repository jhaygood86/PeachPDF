# `CssLayoutEngineTable.ClipColumnsToMaxWidth`/`ShrinkColumnsToFitAvailableWidth` are untestable dead code

Not a spec deviation - `max-width` on a table is observably respected (see
`CssLayoutEngineTableTests.TableLayout_MaxWidthNarrowerThanExplicitWidth_RespectsMaxWidth`). This is a
test-coverage gap: the two methods that are supposed to be *how* that happens can never actually run,
so no HTML/CSS input can drive real coverage onto their lines.

Root cause: `CanReduceWidth(int columnIndex)` (`CssLayoutEngineTable.cs`, unrelated pre-existing code)
has its bounds check backwards -

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

`ClipColumnsToMaxWidth` is a separate case: its caller guard in `EnforceMaximumSize`
(`if (maxWidth < widthSum) ClipColumnsToMaxWidth(maxWidth);`, evaluated *after* columns are already
lowered to their content minimum) does not go through `CanReduceWidth` at all, so it isn't provably dead
the same way. Confirmed empirically, not just by reading the code: `min-width` wider than `max-width`,
unbreakable (`white-space: nowrap`) content wider than a tiny `max-width`, and an explicit `width` wider
than `max-width` were all tried as ways to reach it - in every case `widthSum` after minimization came
out `<= maxWidth` (the guard's condition never true), and the table's final width tracked something
other than what the clip arithmetic would produce, so the method's lines never lit up in coverage
despite the rendered width visibly respecting `max-width`. Most likely explanation: whatever computes
each column's content-minimum width (`GetColumnsMinMaxWidthByContent`) already caps it at-or-below
`max-width` in every scenario tried, so the guard's premise (minimized columns still summing wider than
`max-width`) never actually arises for this layout engine's minimum-width calculation - not confirmed by
stepping through a debugger.

Both methods are marked `[ExcludeFromCodeCoverage]` with this file linked in a `<remarks>` since no test
input found actually drives real coverage onto either.

**Deliberately out of scope.** `CanReduceWidth`'s bug is a correctness issue unrelated to the change that
found it (a CRAP-score reduction pass, not a bug-fixing one). Getting real coverage on these methods
needs either fixing `CanReduceWidth` (a behavior change requiring its own verification), understanding
exactly why minimized columns never sum past `max-width` in practice, or a lower-level harness that
inspects `_columnWidths` mid-layout, which this codebase's table tests don't currently have (they assert
on final rendered geometry, per this repo's layout-testing convention). All three are a larger investment
than a coverage-gap note.
