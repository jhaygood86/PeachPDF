# `@footnote { height }` now reserves a fixed note-area band

**Before:** `@footnote` read only `border-top`, `margin-top`, `padding-top` and `max-height`. A
declared `height` was silently ignored - the note area was always sized to its content, and there was
no way to ask for a fixed band.

**Now:** `height` sets the note area's **content** height: the band the footnote bodies stack in,
with `margin-top`/`border-top`/`padding-top` added on top of it, matching the box the three
already-supported longhands describe. A stack shorter than the declared height reserves the whole
band anyway and leaves the slack below the last body, exactly as a fixed-height block box top-aligns
its content; a taller stack overflows it. `height: auto` is the default and unchanged.

**Two things a document author could notice beyond the new property working:**

- **`max-height` now compares against the content box, not the whole area.** Previously the
  `footnote-policy` "doesn't fit" test compared `max-height` against the area *including* its
  margin-top + border-top + padding-top; now it compares against the same content height `height`
  refers to. With PeachPDF's default chrome that moves the threshold by 9pt, so a document sitting
  within ~9pt of its declared `max-height` can stop (or start) triggering `footnote-policy: line`/
  `block`. This was necessary for the two properties to mean the same box - otherwise
  `@footnote { height: 50pt; max-height: 50pt }` would report a page that cannot fit the area the
  author just sized exactly.
- **Content overflowing a declared `height` now counts as "doesn't fit"** for `footnote-policy`,
  which is css-gcpm-3's own "cannot be placed on the current page due to lack of space" - the note
  area is full whatever room the page has left. Under the default `footnote-policy: auto` nothing
  changes: a too-tall stack simply overflows, as an over-tall single body already did.

**Also:** percentages on `height`/`max-height` now resolve against the page's own content band rather
than the content *width*. `max-height: 50%` previously meant half the content width, which was a bug
for a block-axis length. `margin-top`/`padding-top` percentages still resolve against the width, as
the CSS box model specifies.

`float` and `column-span` on `@footnote` remain parsed and ignored - the note area is always
page-bottom and full-width - and that is now stated in the docs rather than left implicit.
