# `column-span: all` is only recognized on a direct child of the multi-column container

`CssLayoutEngineColumns.cs` (via `BuildSegments`) and the new break-before hooks in
`CssBox.LayoutBlockChildren` only recognize `column-span: all` on a box that is a **direct** in-flow
child of the multi-column container itself — the box whose own `LayoutBlockChildren` loop is directly
inside the column fill (`ReferenceEquals(this, spanContext.ContextRoot)`). A `column-span: all` declared
on a deeper descendant (a child of a child of the multicol container, and so on) has no effect: it lays
out as an ordinary in-column box. Per
[CSS Multi-column Layout Module Level 1 §3](https://www.w3.org/TR/css-multicol-1/#propdef-column-span),
the property should have an effect wherever it's declared, so this is a real, if narrow, deviation.
Tracked as [#625](https://github.com/jhaygood86/PeachPDF/issues/625).

**Deliberately out of scope** of the #602 implementation that gave `column-span: all` its layout effect
in the first place. Recognizing a nested descendant would need the break-before decision to propagate up
through ancestor `PendingBreakToken` chains to the multicol container's own loop — plausible, since
`break-before: column` already propagates that way (`CssBox.cs`'s `childBox.PendingBreakToken is { }
childToken` branch) — but telling apart "this propagated break is a column-span handoff, not an ordinary
forced break" at every level in between, and correctly excluding everything from the spanning box's own
container down to (but not including) the multicol container's direct child from the run before it, is
real additional work the direct-child case did not need.

This matches this engine's existing atomic-per-top-level-child model elsewhere (each of a multi-column
container's own direct children is what balancing, measurement, and the fragment-tree's per-run rule
segments already operate over) — a nested spanning descendant is not a new *kind* of gap so much as the
existing model's boundary, one level further down than #602 reached.
