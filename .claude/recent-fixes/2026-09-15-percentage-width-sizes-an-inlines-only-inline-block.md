# Percentage width sizes an inlines-only inline-block

Issue #1097. An inlines-only `display: inline-block` took the surrounding line-flow path, where
`ResolveAtomicInlineDeclaredWidth` explicitly declined a percentage `width`; the box consequently
shrink-to-fit even though CSS 2.1 §10.3.9 reserves that behavior for `width: auto`.

## Load-bearing idea

The placement code already has both facts the resolution needs: the child exposes its real
`ContainingBlock`, and the line cursor exposes the document Y where the box is being placed. Passing
that Y into `ResolveAtomicInlineDeclaredWidth` lets it use `PageAwareWidthBasis` — the same basis as
`GetBoxWidth` — before assigning the one used width to `CssBox.Size`. The existing cursor advance and
painted-rectangle correction continue to read that single assignment, preserving their `box-sizing`
arithmetic.

The containing block must come from the child, not the `parent` parameter: `parent` is merely the
inline box whose children `FlowBox` is walking and can itself be an inline wrapper. The current line Y
also matters under per-page horizontal reflow, where the same containing block can have a different
available measure on another page.

The percentage basis is passed for every valid declared length rather than only strings ending in
`%`. Absolute lengths ignore it, while `calc()` expressions containing percentage leaves need it.

## Evidence

`InlineBlockDeclaredWidthTests.APercentageWidthReservesItsContainingBlockRelativeWidth` pins the line
advance at 200pt for `width: 50%` inside a 400pt containing block.
`InlineBlockDeclaredWidthGeometryTests.APercentageWidthSizesThePaintedBoxFromItsContainingBlock` pins
the painted box at the same 200pt. The focused test classes pass on net8.0 and net10.0. The full
net8.0 suite passes (11,714 passed, 9 platform-specific skips), and Cobertura records hits on every
changed executable line. The full 131-showcase corpus also generates successfully; the
`atomic_inline_width` showcase now includes a 50%-of-400pt example.
