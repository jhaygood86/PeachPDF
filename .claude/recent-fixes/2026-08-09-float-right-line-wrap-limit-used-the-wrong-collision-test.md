# `float:right` now narrows line-wrap width instead of letting text overlap it

## The load-bearing idea

`DomUtils.GetLastRightIntersectingFloatBox` - called once per word from `CssLayoutEngine.FlowBox`
(`CssLayoutEngine.cs:1595`) to compute `actualLimitRight`, the right-hand wrap boundary for the
current line - queried `GetFirstIntersectingFloatBox` in `Floating.Left` mode even though it exists
to answer a right-float question. `IsFloatIntersecting`'s `Floating.Left` branch tests whether the
cursor's *current* X position is already inside a float's horizontal span - correct for a left
float, which text starts flush against and must be pushed right past the moment it walks into one,
but structurally incapable of detecting a right float in advance: the cursor only enters a right
float's span after the overlap has already happened. Words were placed straight through
`float:right` boxes instead of wrapping before reaching their left edge.

The two kinds of float constrain a line in different shapes, not just on different sides: a left
float caps where the cursor itself currently sits (a point-collision, answered correctly by
`GetLastLeftIntersectingFloatBox`'s existing point-collision-then-push loop, left unchanged), while
a right float caps how far right the cursor is *allowed to reach in advance* - a lookahead,
independent of the cursor's current position or the word being placed. `IsFloatIntersecting`'s
`Floating.Right` branch does not compute this lookahead either; working through the arithmetic it
would actually be given by this caller, it reduces to "the float's left edge is far to the right of
the word" (true when the float *isn't* an obstruction), because that branch exists for a different
caller (`CssLayoutEngine.FloatBoxRight`, placing a *new* float:right box) and was never a fit for
bounding in-flow text either.

The fix replaces the whole method with a dedicated single-pass scan
(`FindNarrowestRightFloatBox`/`ScanForNarrowestRightFloatBox`, same ancestor/preceding-sibling
traversal shape as `FindIntersectingFloatBox`) that finds, among every `float:right` box whose
vertical span covers the current row, the one with the smallest left edge
(`Location.X - ActualMarginLeft`) - the actual binding constraint, computed without reference to
where the cursor currently is or how wide the word being placed is. That let the `referenceWidth`
parameter and the old method's 10000-iteration collision-retry loop both go away: a single
recursive pass already finds the global minimum, so there is nothing left to retry.

## Left floats were checked, not assumed

Every call site of `GetLastLeftIntersectingFloatBox` (`CssLayoutEngine.cs` lines 1550, 1658, 1714)
re-runs it at line start, after every wrap, and after every word placement, always to re-push
`coordinates.CurrentX` past whatever left float currently occupies that exact position. That is
the point-collision shape a left float actually needs, unchanged by this fix. A new
`FloatLeft_StillNarrowsLineWrapWidth_AfterTheRightFloatFix` test locks this in explicitly rather
than leaving it as an unverified assumption riding on the right-float fix.

## Evidence

- New `FloatRight_NarrowsLineWrapWidth_SoTextWrapsBeforeReachingIt`
  (`FloatLayoutRegressionTests.cs`): confirmed failing before the fix (a word landed at
  `Rectangle.Right=224.36`, past the float's left edge at `206`), passing after.
- New `FloatLeft_StillNarrowsLineWrapWidth_AfterTheRightFloatFix`: passed both before and after,
  confirming the left-float path was already correct and stayed that way.
- Full `net8.0` suite and `dotnet build PeachPDF.slnx -t:Rebuild` (0 warnings) both green after the
  change.
