# font-size: calc() with parent-relative units is computed at cascade time (#2003)

**Symptom:** `<div style="font-size:20px"><div style="font-size:calc(2em + 10px)"><div style="width:2em">` gave the
innermost div a 110px font (the calc applied again against 50px) where browsers give 50px.

**Cause:** `InheritStyle` adopts the `Font` area by reference, so a child carried the parent's *declared* `calc()`
text and `DerivedStyle.ActualFont` resolved it against the child's own parent. Plain `em`/`%` were already made
absolute in `CssBox.ResolveFontSizeValueComputation` for exactly this reason; `calc()` was excluded wholesale.

**Fix:** `ResolveFontSizeValueComputation` now also computes a `calc()` eagerly when every unit in it is absolute or
parent-relative (`em`/`ex`/`ch`/`cap`/`ic`/`lh`/`%`), via `IsParentRelativeOnlyCalc`. A calc using `rem`, a root-element
unit, a viewport unit or a `cq*` unit stays lazy (it needs a layout-time basis, and is not parent-relative so it
cannot compound). The root element, with no parent, resolves against the default font size.

**Trap:** a `calc()` is ONE `Function` token whose operands live in `ArgumentTokens`; scanning the top-level token
list for `Dimension` tokens sees nothing and silently accepts `rem`/`vw`. The first version did exactly that.

**Not done:** a lazy calc (rem/viewport mixed with em) still re-resolves in inheriting descendants; that needs a
"declared vs inherited" marker on the font area and was left out as rarer.

**Evidence:** `PeachBrackets.Engine.Tests` 16950/16950, new tests in `FontSizeInheritanceIntegrationTests` and
`FontSizeTypedStorageTests` (which previously pinned the lazy behaviour for an em calc).
