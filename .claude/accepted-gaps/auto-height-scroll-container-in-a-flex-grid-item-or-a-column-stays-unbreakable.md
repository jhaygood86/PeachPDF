# An auto-height scroll container in a flex or grid item, or in a multi-column container, stays unbreakable

An auto-height `overflow: hidden`, `auto` or `scroll` box in ordinary block flow breaks between its lines
across pages. Two kinds of box are still treated as monolithic, moved whole to the next fragmentainer when
they would straddle the edge, as every scroll container was before:

- one that is a **flex or grid item**;
- one that sits **inside a multi-column container**.

`MonolithicContent.IsLaidOutByAnEngineThatCannotContinueIt` says so. Letting them break was tried and
measured, on a 300pt x 200pt page with a 12pt line:

- a flex or grid item: with a lead paragraph of 82 to 90 words the item straddles the page edge and a whole
  8-word paragraph is lost (upstream loses none; Chrome draws every word). The flex/grid commit pass pins the
  item's used size (`ItemContentCommit`, `ItemContentSizeEverPinned`) before the item's own layout, so a
  classifier that asks for the box's authored height answers "definite" at that point and "auto" earlier, and
  the item is then laid out with breaking suppressed and clipped at the band edge;
- inside `columns: 2`: an auto-height `overflow: hidden` box holding 12 paragraphs drew 8 of 96 words, and page 1
  was blank (the box's clip rectangle is applied per fragment while the box now spans columns);

`overflow: auto` and `scroll` items behave the same as `hidden` in the flex/grid case; in columns only `hidden`
lost text.

A fix means remembering whether the item's height was automatic before the pin and resolving the overflow
clip from the finished fragment for a box that continues, so the classifier can stay a function of the box's
own style. That is more than the change that introduced this note.

A scroll container that holds an absolutely positioned box used to be a third kind. It is not any more: an
absolute box runs as passes of its own and no longer ends its container's, so such a container breaks like
any other.

Tracking issue: #1528.
