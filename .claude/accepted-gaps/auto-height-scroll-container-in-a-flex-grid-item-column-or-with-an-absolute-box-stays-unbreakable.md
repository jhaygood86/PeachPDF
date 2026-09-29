# An auto-height scroll container in a flex or grid item, in a multi-column container, or holding an absolute box stays unbreakable

An auto-height `overflow: hidden`, `auto` or `scroll` box in ordinary block flow breaks between its lines
across pages. Three kinds of box are still treated as monolithic, moved whole to the next fragmentainer when
they would straddle the edge, as every scroll container was before:

- one that is a **flex or grid item**;
- one that sits **inside a multi-column container**;
- one that **holds an absolutely positioned box** (a `display: none` one does not count), whatever its own
  `position`.

`MonolithicContent.IsLaidOutByAnEngineThatCannotContinueIt` says so for the first two and
`MonolithicContent.HoldsAnAbsolutelyPositionedBox` for the third. Letting them break was tried and measured,
on a 300pt x 200pt page with a 12pt line:

- a flex or grid item: with a lead paragraph of 82 to 90 words the item straddles the page edge and a whole
  8-word paragraph is lost (upstream loses none; Chrome draws every word). The flex/grid commit pass pins the
  item's used size (`ItemContentCommit`, `ItemContentSizeEverPinned`) before the item's own layout, so a
  classifier that asks for the box's authored height answers "definite" at that point and "auto" earlier, and
  the item is then laid out with breaking suppressed and clipped at the band edge;
- inside `columns: 2`: an auto-height `overflow: hidden` box holding 12 paragraphs drew 8 of 96 words, and page 1
  was blank (the box's clip rectangle is applied per fragment while the box now spans columns);
- a `position: relative; overflow: hidden` wrapper holding six 8-word paragraphs and a 6-word
  `position: absolute; top: 30pt; right: 0` box, after a 60-word paragraph: the wrapper straddles the page
  edge, and the absolute box's 6 words are drawn above the page area and lost (upstream draws them all).
  The same document with `overflow: visible` loses them on upstream too, so this is an existing defect of an
  absolute box whose containing block spans a page break, which only becomes reachable for an overflow box
  when it may break. A wrapper with no `position` of its own loses them the same way when the absolute box is
  placed against an ancestor above it. A wrapper without an absolute descendant is unaffected and does break.

`overflow: auto` and `scroll` items behave the same as `hidden` in the flex/grid case; in columns only `hidden`
lost text.

A fix for the first two means remembering whether the item's height was automatic before the pin and
resolving the overflow clip from the finished fragment for a box that continues, so the classifier can stay a
function of the box's own style. A fix for the third belongs in how an absolute box is placed when its
containing block continues onto another page. Both are more than the change that introduced this note.

Tracking issue: not yet filed. This note was recorded offline; file the issue upstream with the three cases
above and add its number here.
