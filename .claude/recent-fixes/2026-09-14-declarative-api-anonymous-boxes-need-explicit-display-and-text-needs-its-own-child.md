# Declarative API: anonymous boxes need explicit `display`, and `Text()` needs its own child box

Two load-bearing findings from actually rasterizing a declarative document (not just checking its
fragment tree), for the new `PdfGenerator.CreateDocument`/`PeachPDF.Layout` declarative
document-building API.

## Every hand-built anonymous box needs an explicit `display` - there is no UA stylesheet to fall back to

`DomUtils.GeneratesFlexOrGridItem`'s empty-anonymous-box exclusion (a separate, earlier fix this same
feature needed) was not the only place a null-`display`-by-default anonymous box could go wrong. CSS's
own initial value for `display` is `inline` - real HTML gets `block`/`table`/`list-item`/etc. from the
*user-agent stylesheet* matching by tag name (`div, ul, ol, li { display: ... }`), which a hand-built
declarative tree never runs any selector-matching against at all (by design - see `HtmlContainerInt.SetDeclarativeRoot`'s
own remarks). Two of this feature's own anonymous wrapper boxes were built without ever setting `display`
explicitly, and both broke silently rather than throwing:

- **`Header`/`Footer`'s running-positioned wrapper box** (`PageDescriptorBuilder.BuildRunningElement`)
  stayed at the CSS-initial `inline`. An inline box's own layout has no notion of "fill the containing
  block's width" the way a block box's `auto` width does, so `MarginBoxContentFragmentBuilder.Build`
  reported a real, correctly-positioned rect (`GetMarginBoxRect`'s own math for `top-center`/
  `bottom-center` was never wrong) but with the wrapper's own measured width collapsed to near-zero -
  rendering the header and footer both glued to the page's top-left corner, overlapping each other,
  regardless of their real (correct) Y position. Fixed by setting `display: block` on the wrapper before
  handing it to the caller's `Header`/`Footer` handler.
- **`OrderedList`/`UnorderedList`'s wrapping box** (`ContainerBuilder.BuildList`) had the same gap -
  fixed the same way.

Every other anonymous box this feature creates already sets its own `display` explicitly for an
unrelated reason (`flex`/`table`/`table-row`/`list-item`/etc. are load-bearing for *what the box is*,
not just how it's positioned), or is a flex item that the flex engine blockifies on its own (CSS
Flexbox §9.2) - so this specific gap was narrow, but the underlying lesson generalizes: **any future
anonymous wrapper box this feature adds needs its own explicit `display: block` (or whatever value is
structurally correct) checked in, not assumed** - there is nothing else that will ever supply one.

## `IContainer.Text(string)` must never set `.Text` directly on a box that might already have a child

The original implementation set `box.Text = text` directly on the wrapped box, reasoning (in its own
doc comment) that this mirrored "an HTML element that holds both its own padding/border and its own
text node." That reasoning was wrong on its own terms: a real HTML element's text is a separate
anonymous **child** text-node box, never the element's own `.Text` - `CssBox` must never hold both real
child boxes and its own words at once (the same invariant PR2's line-clamp work already depended on).

This went unnoticed until `IListDescriptor.Item()` was added: a list item's synthesized `::marker`
(`ListDescriptorBuilder.Item`, built to mirror `DomParser.EnsureListItemMarkers` for a hand-built tree)
is a real child of the item box *before* the caller's own handler ever runs. Calling `.Text(...)` inside
that handler set the item's own `.Text` while it already held the marker as a child - an invalid,
silently-broken state. The visible symptom was severe and non-obvious: not just a missing marker, but
the item's own text disappearing too (only a degenerate sliver of the marker glyph painted at all).

Fixed by making `Text(string)` always create a new child box for the text (matching `Span`'s existing
shape) instead of ever touching `box.Text` directly - unconditionally, not only when a pre-existing
child is detected, since a future caller pattern could introduce another such child this fix should not
need to special-case.

## Why this evaded the layout-level tests already written

Every declarative-API test written before this rasterization pass proved the right *box* was reachable
(same-object-identity through the fragment tree, marker shape/counter value resolved correctly) but
never checked its *resolved geometry* - the reason `CLAUDE.md`'s testing conventions call out rasterizing
as necessary, not optional, for anything paint-related. Two tests were strengthened accordingly
(`HeaderAndFooter_RepeatAsRealMarginBoxContentOnEveryPage` now asserts non-zero rect width and a real Y
ordering between header and footer; `UnorderedList_DefaultsToDiscMarker` now asserts the marker's
`Location.X` is on-page) so a regression here fails a fast unit test instead of only a showcase render.
