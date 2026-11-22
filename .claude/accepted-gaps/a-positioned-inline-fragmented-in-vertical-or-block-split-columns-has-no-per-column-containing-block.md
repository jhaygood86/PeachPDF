# An absolute box in a positioned inline split over columns is sliced per column only for plain horizontal inlines

Tracking issues: [#2446](https://github.com/jhaygood86/PeachPDF/issues/2446) (vertical writing modes) and
[#2445](https://github.com/jhaygood86/PeachPDF/issues/2445) (block-in-inline split in columns).

`CssLayoutEngineColumns.RestateFragmentedAbsolutes` gives the box a slice per column (css-position-3 §2.1,
css-break-3), for ltr and rtl horizontal flows. Two cases keep the old single rectangle:

- **vertical-rl / vertical-lr multi-column containers** are laid out by `CssBox.TryLayoutVerticalColumnsAsync`
  (one long strip cut afterwards), which records no `ColumnFragmentainerRecord`s and whose block flow does not form
  a containing block from the inline's fragments. WPT `position-absolute-in-inline-008a/b`, `010`, `012` fail.
- **a block-in-inline split** (007b, 008b) is laid out into the wrong columns before the box is involved.

The slice model is axis-agnostic in principle (inline edges measured from each column's start, block edges cut at
the column bands), so the vertical case is the same computation with the axes swapped once the vertical flow
supplies the fragments and column areas.
