# An outside marker that hangs past the content edge is no longer clipped away

`<ol style="display:contents"><li>a<li>b</ol>` drew `a` and `b` with no `1.`/`2.`. Numbering was right (the
marker existed, with the right text); the marker was **cut off**. An outside marker hangs off its item's
inline-start edge (CSS Lists 3 §3.1), in whatever room the item's margin or padding leaves. `display: contents`
removes the `ol`'s box and its 40px indent with it, so the `li` sits at the content edge, the marker lands at
`ClientLeft - width - 5` to the left of it, and `FragmentPainter.Paint` pushed the page clip at the content-area
edge. The same happens to any `ul/ol { margin: 0; padding: 0 }`. `MarkerFragmentPainter` does no visibility test,
so nothing reported it.

## What was found by running it

- **Nested and wrapped contents lists looked fine**, which pointed at counters first: they keep some indent (the
  parent `li`'s, or a wrapper's), so the marker lands inside the clip. Only a *top-level* one lost its markers.
  The tell was the same list under a 60pt page margin: still no numbers with 60pt of room on their left - room
  the page clip, not the page, was refusing.
- **Every existing list test indents the list by 40pt**, and `FragmentPaintHarness.PaintBox` paints without the
  page clip at all, so nothing could see it. `OutsideMarkerPageClipTests` uses `PaintPage` and compares each
  marker's draw position with the clip that was actually pushed.

## The change

`FragmentPainter.Paint` already widened the page clip for an outline; that measure is now `MeasurePaintReach`,
which also takes how far an outside marker overhangs the clip's left edge (its word rect, or its image rect for
`list-style-image`). Only the page-level clip widens; an ancestor's or its own `overflow` clip still constrains
the marker. Left side only: `CssBoxMarker.PerformLayoutImp` never mirrors a marker for `direction: rtl`, so
nothing hangs past the right edge. A right-hand widening was written first and removed when its own test showed
the marker never goes there.

The widening is capped at the sheet's own edge: a list moved off to the left by the visually-hidden idiom
(`left: -9999pt`) would otherwise have widened the clip by ~10,000pt and let everything else overhanging the margin
on that page paint (found by review; `MarkerHungFarOffTheSheet_*` reads -9988 without the cap).

## Deliberately not done

- **A marker in a footnote body flush against the content edge is still clipped.** `PdfGenerator.PaintFootnoteArea`
  pushes its own clip, which this does not widen. Not reached by any known document; widen it the same way if one
  turns up.

- **rtl markers are still laid out on the left.** That is a limitation of marker layout, not of the clip, and no
  test pins a side for rtl.
- **No layout change.** The marker position is what the spec says; only the clip was wrong.
