# The shift of a line below a float is bounded by the page for a container's own floats

#1563 let a line be shifted below a float up to the page's foot (not the column's trial band) only when the float left the
column no inline room at all, because relaxing it further doubled a float's text in the next column. That doubling was the
stale float snapshot, now fixed, so the bound can follow the rule it was always meant to: a column's own band is a poor
bound for a balanced container, whose band starts as an even share of its content.

`<div style="columns:3"><p>8 words</p><div style="float:left;width:80pt;height:40pt"></div>w20 w21</div>` (81pt columns)
left `w20 w21` 1pt of room beside the float: they were placed at the column's right edge and drawn on no page.

**Rule.** The page's foot is the bound whenever the lines are crowded by floats of the same multi-column container as the
block being flowed (`ColumnsContainerOf`). A float from a container nested in a column of it keeps the trial band: a
document with a nested container lost and doubled words when the lines were shifted past its floats, which belong to the
inner container to place (nested multi-column stays an open area).

Found by logging, not by reading: a differential reduction (the relaxed bound against the old one, switched in-process) of
the one corpus document the first attempt made worse (seed 112) reduced to the nested container.

Evidence: `TextWithAFewPointsOfRoomBesideAFloat_MovesBelowIt` (fails without the change); full net8.0 suite passes; 300-document
corpus unchanged against the stale-float fix (none worse, none better).
