# MathML elementary math (mstack/mlongdiv) is not supported

`mstack`, `mlongdiv`, `msgroup`, `msrow`, `mscarries`, `mscarry`, and `msline` (MathML 3 §3.5, the
"Elementary Math" module for column-aligned arithmetic and long division) have no dedicated `MathNode`
type or layout support in `MathTreeBuilder`/`MathLayoutEngine` — an unrecognized element degrades to a
plain row of its children, which does not reproduce this module's column-alignment/carry/borrow
semantics at all.

This mirrors [MathML Core](https://w3c.github.io/mathml-core/)'s own scope: Core dropped elementary math
entirely, so there is no browser reference implementation to check a from-scratch implementation against.
The module is also rare outside elementary-education material. See
[Supported MathML Features](../../docs/supported-mathml-features.md#elementary-math).
