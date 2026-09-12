# Several MathML presentation attributes are parsed and carried on the tree but not yet acted on by layout/paint

A cluster of related, individually-small v1 simplifications in `MathTreeBuilder`/`MathLayoutEngine` -
each attribute below is correctly parsed onto its `MathNode` (so a future change can act on it without
touching the parser), but has no effect on the rendered output yet. These are all confirmed to be
genuinely **absent from MathML Core** itself (not just unimplemented here) - grepping the spec text finds
no `menclose`, `mlabeledtr`, `bevelled`, `columnalign`, or `rowalign` anywhere in it. Chromium and WebKit,
which both target Core exclusively, don't render any of these either; only Firefox differs, via
pre-Core legacy behavior it kept for compatibility. Since the project's own scope is MathML Core rather
than full MathML 3 (see the original implementation plan), these stay accepted gaps:

- **`mfrac[bevelled="true"]`** (`MathFractionNode.Bevelled`) - renders as an ordinary (non-slanted)
  fraction; the slanted-bar presentation is not implemented.
- **`menclose`**'s `notation` (`MathEncloseNode.Notation`) - renders as a plain pass-through of its
  content; no enclosure mark (box, circle, strikethrough, radical, ...) is drawn.
- **`mtable`/`mtr`'s `columnalign`/`rowalign`** (`MathTableNode.ColumnAlign`/`RowAlign`,
  `MathTableCellNode.ColumnAlign`/`RowAlign`) - parsed but every cell centers regardless of the requested
  alignment (`LayoutTable`'s cell-positioning math always divides the remaining column/row space by two).
- **`mlabeledtr`'s label cell** (`MathTableRowNode.Label`) - parsed and kept off the row's own `Cells`
  list correctly, but never laid out or drawn (equation numbering).
- **`MathKernInfo`** (the OpenType MATH table's per-glyph corner-kerning data for sub/superscript
  placement) - not read at all (`MathTable.cs`'s own header comment records this as an intentional
  parser-level scope cut, "a fine-grained spacing refinement, not required for correct layout"); confirmed
  optional even under MathML Core itself (its script-layout algorithm never references `MathKernInfo`).
  Scripts are still correctly shifted/sized via `MathConstants`, just without the extra kerning a
  `MathKernInfo`-aware engine would add.

Two items formerly listed here were removed after checking the actual spec text rather than assuming it:
`mathvariant`'s automatic-italic default is implemented (MathML Core §4.2/Appendix C.1's `math-auto`
transform - see `MathItalicMappings`/`MathTreeBuilder.ApplyAutomaticItalic`); Core does not define any
other `mathvariant` value (bold/double-struck/etc. - authors are told to use the corresponding real
Unicode characters directly instead), so ignoring them was already correct, not a gap. And
`munder`/`mover`/`munderover`'s accent default was previously (incorrectly) described here as consulting
the base's core-operator `accent` property; MathML Core §3.4.2 actually says the opposite - an
unspecified/invalid `accent`/`accentunder` attribute is simply treated as `false` - so PeachPDF's
existing default (moving-limit/scriptlevel-shrink treatment) was already spec-compliant. `mpadded`'s
`width`/`height`/`depth`/`lspace`/`voffset` were also removed from this list - MathML Core §3.3.6 is a
real, all-three-engine-supported requirement (unlike the items above), and is now implemented in
`MathLayoutEngine.LayoutPadded`.

See [Supported MathML Features](../../docs/supported-mathml-features.md#accepted-gaps) for the
reader-facing summary of the same list.
