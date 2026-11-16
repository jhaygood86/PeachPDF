# The quirks-mode margin and height quirks are for horizontal writing

HTML 15.3.9 zeroes `margin-block-start` and `margin-block-end`, and the Quirks Mode Standard 3.6 and 3.7 size `html` and `body` in the block direction. PeachPDF writes the margin rules with the physical `margin-top` and `margin-bottom` (a logical declaration in the user-agent sheet would defeat every author's physical margin, see the `hr` rule in `CssDefaults.cs`), and sizes `html` and `body` only for `horizontal-tb`. In a vertical writing mode the margin quirks zero the wrong edge (the top and bottom of the physical box), and `html` and `body` are not filled. The standard also leaves a document whose `html` and `body` are in different writing modes undefined.

The `:hover`/`:active` quirk is applied to a rule whose whole selector is a bare `:hover`/`:active`, or a compound of nothing else, as the standard says; it is also applied to such a compound inside the argument of `:is()` or `:not()`, which the standard exempts.

Part of [document modes](../../docs/html-css-support.md#document-modes-quirks-mode).
