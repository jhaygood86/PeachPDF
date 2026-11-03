# hanging-punctuation end values (force-end, allow-end, last)

Load-bearing idea: do not touch the wrap machinery beyond one rescue in the `overflows` test. A stop/comma word that overflows only by its last character is re-tested without that character; if it then fits it stays and records `CssRect.HungEndWidth`. A word that is line-final and fits whole is marked at line close (`ApplyEndHang`, called from `FinalizeLineBoxes`, which now takes the canvas for measuring), and `LineContentEnd` subtracts the hung width so alignment/justification see the shorter line. Nothing else moves, so the paint is unchanged: the character is drawn in place and overhangs.

Traps found by running it: tests that assumed monospace is 6pt at 10pt were wrong (the substitute font measures 5.498pt), so tests measure a character instead. The hung-width check for a trailing border/padding must walk the inline ancestors the word closes - the word's owner is often an anonymous text box with no padding of its own.

Not done: see the accepted gap for `last` not affecting breaking and for RTL/vertical lines.
