# Letter-spacing is not applied after the last character of a line

css-text-4 section 7.2: the spacing goes *between* adjacent characters. The engine measures it inside the text (each character's advance carries the spacing), so a word's width includes the spacing after its last character, which shifted a right- or center-aligned line and made a line wrap earlier than it should. A word that ends a line now leaves it out, the way a hanging space separator is left out: `CssLayoutEngine.TrailingLetterSpacing` / `MeasureLineEndOverhang`, used (1) in the fit check in `FlowBox`, (2) in `ApplyEndHang` (sets `HungEndWidth`, which `LineContentEnd` subtracts for alignment) and (3) in `CssBox`'s intrinsic walk (`trailingSpace` for max-content, the run's end for min-content).

**Measured:** css-text 918 -> 920 on a Mac (`letter-spacing-200`, `-205`; floor 892 -> 894).

**Not done:** `letter-spacing-end-of-line-001` still differs - the float's shrink-to-fit width comes out one spacing too wide (the third `a` lands under the 5, not the 4) even though the intrinsic walk now reads the trailing spacing (checked: the value is read at 28.8). Whatever sizes a float after layout (not `GetMinMaxWidth`) has to be taught the same. Also open: `-201` (spaces), `-203`/`-204` (element boundaries), `letter-spacing-nesting-*`, the bidi/cursive/combining-mark ones, `letter-spacing-percent-001`.
