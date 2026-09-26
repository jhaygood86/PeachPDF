# A scroll container that clips past its max-height is kept whole

_CSS Fragmentation Level 3 §2 (monolithic content). Tracker: [#1375](https://github.com/jhaygood86/PeachPDF/issues/1375)._

§2 lets a UA keep an `overflow: hidden` box in one piece only when it has "a non-auto logical height (and
no specified maximum logical height)", and makes the same optional for `auto`/`scroll`.
`MonolithicContent.HasConstrainedBlockSize` takes that wording for every overflow value, so a scroll
container capped by `max-height` alone (or a hidden one by `aspect-ratio`) breaks like a plain block, as
Chrome prints it. Only the `hidden` case is a spec deviation; for `auto`/`scroll` keeping it whole is
allowed, and this matches Chrome instead.

The exception is such a box whose content overflows the cap. Its clipped lines lie past the box's end, and
a page break among them ended the layout pass there: the content after the box was placed back on the page
the break left, which was already emitted. Measured on a 300×200pt page, a `max-height: 60pt` box holding
30 lines lost all ten lines after it, and a `max-height: 96pt` box straddling the boundary lost the three
after it and added an empty page.

So `CssBox.NoteIfAFragmentingScrollContainerClips` records such a box after `ApplyHeight`
(`HtmlContainerInt.ScrollContainersThatClip`), and `PerformLayout` lays the document out again with it
monolithic: it moves whole, or is sliced when taller than a page (with
[the capped-box slice loss](a-capped-scroll-container-taller-than-a-page-loses-its-boundary-lines.md)).
Boxes only join the set within one layout, so the retry settles; it is bounded at three attempts. The set
is cleared at the start of each layout (`PerformLayoutOnePass`): a box widened since the last one may fit
under its cap and must be allowed to break again. A review caught it surviving across layouts.

Breaking it properly needs the capped height to count the block size consumed across fragments (the clamp
measures `Location.Y + max-height` in document space, which includes the space a break leaves unused at the page foot; `StraddlingScrollContainerCappedByMaxHeight_BreaksOnlyWithRoomForTheSpaceLeftAtThePageFoot` pins it), and the clipped lines to
be laid out without ending the pass for the content after the box.

The retry runs inside every `HtmlContainerInt.LayoutDocument` call, not once after the first: a per-page
width reflow, the footnote and page-float loop or a `target-counter` reflow lays the document out at a
different geometry and can make a box clip that the first layout did not (a review of #1334 found it ran only
once). Vertical writing modes keep the older rule (any capped block size is monolithic).
