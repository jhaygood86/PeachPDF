# Text beside a float wider than its column moves below it (#1560)

`<div style="columns:3"><p></p><div style="float:left;width:92pt;height:32pt"></div>w</div>` (81pt columns) drew `w`
on no page: it was placed beside a float that left the column no room, past the column's right edge, where no column
claims it. #1554's change (the lines beside a float wider than the column move below it, inside a column too) was the
first half; this is what made it work in a balanced container.

## Three things, found by running it

- **The shift gave up.** `ShiftEmptyLineBelowCrowdingFloats` takes no shift that ends below the fragmentainer being
  filled. A balanced container's column band starts as an even share of its content (16pt here), shorter than the float
  (32pt), so it never shifted. When the float leaves the column no inline room at all (`startX >= limitRight`) the
  bound is the *page's* foot instead, for a column that is not itself inside another column. A float that leaves some
  room beside it keeps the trial-band bound: widening that case made a document double a float's text in the next
  column.
- **Then the container deferred itself for ever.** With the line shifted, the float (32pt) and the line under it (12pt)
  need a 44pt column; the trials grow the band by a fifth each (16, 20, 25, 31, 38 and the attempt cap), so the fill
  carried the float over, the next page started the same trials, and layout never ended (a hang, found only by
  running it: the suite passed up to it). On a carry the band now grows to at least the content's reach
  (`contentBottom - boxTop`), so a container holding a float reaches 44pt in four fills.
- **The growth must be limited to containers that hold a float.** Applied to every balanced container it doubled whole
  paragraphs in a document with a container inside another (41 words): the jump changes the trials the outer fill
  repeats. `holdsAFloat` is a float child of the container or of its anonymous wrapper.

## Not done

A fallback fill at the whole page budget when the trials run out also fixed the hang, and doubled the same nested
document, so it was dropped. A float taller than the page is still the accepted gap for floats that do not fit.

Evidence: `MulticolFloatWiderThanColumnTests` (the reduction fails without the change, and a timeout guard catches the
deferral); 300-document corpus against main: 0 documents lose more words or double more, 7 lose fewer and 3 double
fewer; full net8.0 suite passes.
