# `display: flow-root` establishes a block formatting context

Until now `display: flow-root` was an unknown value: the declaration was dropped and the box kept the `display` it
had (a `div` stayed a plain block), so the one-line containment idiom did nothing, and the docs said "not implemented".

**Fix.** `DisplayMode.FlowRoot` (mapped from `flow-root` in `Map.DisplayModes`). `DerivedStyle.ActualDisplay` reads it
as `block`, so every layout path that dispatches on the display keyword treats it as the block it is; only the
formatting-context predicates tell it apart (`DerivedStyle.IsFlowRoot`):

- `DomUtils.EstablishesIndependentFormattingContext` (so it contains its floats, CSS 2.1 §10.6.7, and the float scans
  stop at it);
- `CssBox.IsMarginCollapseThrough` (an empty flow-root's margins do not pass through it) and the block-start margin
  chain walk (its first child's margin stays inside it). The block-end side already asked
  `EstablishesIndependentFormattingContext`.

**Found by running it.** The first version passed the float test and still collapsed a child's margin through the
box: `overflow: hidden` isolated the margin at three call sites that each test `Overflow` by name instead of asking the
formatting-context predicate, so the same box with `display: flow-root` collapsed (50pt where `overflow: hidden` gave 55).
The chain walk is the one that mattered; the other `Overflow` tests there are vertical-writing-mode paths.

**Not done.** The margin and containment call sites still test `overflow` directly rather than the shared predicate,
so any other formatting-context root (a multi-column container's first child, an inline-block's) can still collapse
its first child's margin outward; changing them all is a wider behaviour change than this feature.

Evidence: `FlowRootTests` (containment, a plain block still not containing, margins kept inside versus collapsed, and an
empty flow-root); a `flow_root` showcase; full net8.0 suite passes; the generated corpus is unaffected (no corpus
document uses it).
