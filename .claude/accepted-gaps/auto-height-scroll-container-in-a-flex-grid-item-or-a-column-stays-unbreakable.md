# An auto-height scroll container in a multi-column container stays unbreakable; a hidden flex or grid item can lose its last line

An auto-height `overflow: hidden`, `auto` or `scroll` box in ordinary block flow breaks between its lines across pages,
and so does one that is a **flex or grid item** (the classifier reads `HeightBeforeItemPin`, the height the author
wrote, not the one the commit pass pinned). Two things are left:

- **A scroll container inside a multi-column container** is still monolithic, moved whole to the next fragmentainer
  when it would straddle the edge (`MonolithicContent.IsLaidOutByAnEngineThatCannotContinueIt`). Letting it break was
  tried and measured: an auto-height `overflow: hidden` box holding 12 paragraphs drew 8 of 96 words, and page 1 was
  blank (the box's clip rectangle is applied per fragment while the box spans columns). `overflow: auto` and `scroll`
  lost nothing there; only `hidden` did. A fix means resolving the overflow clip from the finished fragment for a box
  that continues.
- **An `overflow: hidden` flex or grid item whose content runs over several pages** can lose its last line to its own
  clip. The commit pass pins the item's height to the unbroken measurement, and every break that moves a line to the
  next page adds the strip it left behind, so the clip ends a couple of points above the last line (measured with a
  30-line panel plus an 8-word paragraph after an 84 to 98 word lead paragraph on a 300pt x 200pt page: the paragraph's
  line is cut by 2.2pt). `overflow: auto` and `scroll` items are not affected, since their overflow is not clipped in
  the PDF. Growing the box to its lines at the end of the commit (tried in `ItemContentCommit.CommitLayout`) did not
  stick: the live bottom the clip reads was back at the pinned value at emission, and where it is reset was not found.

A scroll container that holds an absolutely positioned box used to be a third kind. It is not any more: an absolute
box runs as passes of its own and no longer ends its container's, so such a container breaks like any other.

Tracking issue: #1528.
