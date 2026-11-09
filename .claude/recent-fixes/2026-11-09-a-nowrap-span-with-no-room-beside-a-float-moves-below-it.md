# A `white-space: nowrap` span that has no room beside a float moves below it (#2294)

`<div style="float:left;width:100%">…</div><p><span style="white-space:nowrap">word</span></p>` drew `word` at the page's
right edge, clipped: lost. Chrome puts it at the left edge below the float.

**Three causes.**

1. The shift that moves a line below the floats that leave it no room (`ShiftEmptyLineBelowCrowdingFloats`, CSS 2.1 §9.5)
   was asked only for words whose box permits wrapping (`WhiteSpacePermitsWrapping`), so a nowrap word was never shifted.
   It is now asked for them too, outside a column fill.
2. The nowrap-run check (`wrapNoWrapBox`: "the run does not fit what is left of the line, so start a new line") reads the
   cursor, which a left float has pushed to the float's right edge, as "something is already on this line". It was then
   set for the first word of the line, and the line it started undid the shift (the word was drawn at the old Y, after the
   float). After a shift that brings the cursor back to the line start, the flag is cleared.

3. The shift was asked about the first word's width, but what cannot be broken is the whole nowrap run: a run of two words whose
   first word fits beside the float has no room there either. `WidthThatCannotBeBrokenFrom` gives the width from the word to
   the run's end (the shift is also asked again when the run wraps onto a line of its own after text, the second call site).

**Found by running it.** A tracing line before and after the shift call: the first showed the word at x=280 and the second
at x=20, y moved below the float, and the output still had the word at (280, 33). Fixing only the first cause changes
nothing visible.

**Not done, and measured.** When the *block itself* is `white-space: nowrap` or `pre` (`<p style="white-space:nowrap">`),
the word is still placed past the float. Headless Edge does the same (the text is beside the float, off the page), so this
is left as it is. Inside a multi-column container nothing changes either (see
[the accepted gap](../accepted-gaps/a-float-wider-than-its-column-covers-the-text-after-it-under-nowrap.md): a line shifted
there is deferred and the line before it drawn twice).

**Evidence.** Corpus A and B (400 generated documents) rendered by `main` and the branch: one document differs (10 → 8
words lost, 7 → 6 doubled), none worse. Tests: `FloatPlacementTests.NoWrapWordAfterAFloat_GoesBelowItOnlyWhenThereIsNoRoomBeside` and
`NoWrapRunAfterAFloat_IsMovedAsAWhole` (a two-word run, and a run that wraps after text; fail without the change, checked by reverting it) and, through `PdfGenerator` for the coverage gate,
`NoWrapAfterFloatPdfTests`.

**Left over.** Two generated documents of the later corpus lose one word more than `main` with the run-width part; their reductions
did not finish and are not explained, so that part is measured on the three reductions only.
