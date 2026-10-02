# A float on the line a column break discards is not claimed by the column it left

A container's first column laid out a float, then the break landed on the line holding it (text after the float did not
fit): the line's words are discarded and re-laid by the next column, and so is the float, but the first column's snapshot
kept the float. Its text was drawn in both columns (a doubled word), and a shift that moved a line below a float made
the case common.

`<div style="columns:3"><p>15 words</p><div style="float:left;width:92pt;height:32pt">w87</div>w92 w93</div>` drew `w87`
at (109,48) and (199,20).

## Cause, found by logging what the emitter saw

Both column instances held the float, with different word rectangles (each snapshot records geometry as it was when the
column was filled). The flow had placed the float while building the line it then discarded; the resumed pass places it
again at the next column's top (`childOpensHere` is true for a float on the resume ordinal), so the float is correctly
in the next column, and wrongly still in the first. Words on the discarded line already say so
(`AwaitsTheNextFragmentainer`); a float is a box, not a word, and nothing did.

## Fix

The flow records the floats it placed while building each line (`CssLineBoxCoordinates.FloatsPlacedOnThisLine`, reset
when the next line opens) and leaves them on the block when a break discards that line
(`CssBox.FloatsOfTheDiscardedLine`). `CssLayoutEngineColumns.BeyondThisColumn` adds them to the boxes the column does
not hold, so its snapshot never captures them, which also covers a float's own background and border, not only words.

## Trap

Excluding the float from the snapshot also removed it from `MaxBottomOf`, which sizes the columns' growth: the growth
rule added in the float-wider-than-its-column fix then ran out of attempts and layout hung again on the reduction (a
timeout guard in the tests caught it, and nothing else would have). The growth now reads the floats' own last bottom
(`FloatReach`) as well as the content's.

Evidence: `FloatOnTheLineThatBreaksToTheNextColumn_IsDrawnInOneColumnOnly` (fails without the change); full net8.0 suite
passes; 300-document corpus against main: none worse, 1 loses fewer words and 1 doubles fewer.
