# A column break can fall below the container's own child

_Landed 2026-07-27._

[Issue #387](https://github.com/jhaygood86/PeachPDF/issues/387),
[CSS Fragmentation 3 §2](https://www.w3.org/TR/css-break-3/#fragmentainer).

#366 made a **top-level child** of a multi-column container split at a column boundary. A block one
level further down did not: on the issue's fixture the wrapper neither moved nor broke, and the
container reported a height past its own page. Measured on `main` before the change, on a 400pt page
with `columns: 2; column-fill: auto` and a wrapper of 24 × 20pt rows: `r18` was claimed by **two**
fragments at once (once in each column, both at y=386, straddling the 400 band), rows 19–23 left
ghost fragments in column 1 at y=406…486, and the flow spilled onto a second page it did not need.

**The load-bearing idea is that everything the columns engine states about "the next column" is
stated on the record's *outermost* link, and a break below the container's own child has a chain.**
Three sites read that record, and each of them was reading one link where it needed the whole chain.

**(a) The target for the box that begins the next column.** The arms that raise a column break
deliberately record no target — every column of a container begins at the same block-axis
coordinate, so `CssLayoutEngineColumns.ResumeInTheNextColumn` supplies it. It can only supply it for
a *top-level* child, though, and a deeper link's box begins its own containing block's content
rather than the column, at a coordinate not knowable when the record is written because that block
has not been re-placed in the next column yet. So the target is now re-derived at placement time:
`CssBox.ColumnTopForTheChildThisFillBeginsAt` gives the child a resume top of
`max(column.ResumeContentTop, ContainingBlock.ClientTop)`, the same maximum
`ResumeInTheNextFragmentainer` takes and for the same reason. Without it `PlaceBlockChild` fell back
to the previous sibling's bottom — a sibling still sitting in the column just vacated.

**(b) The whole chain is restated, not only the outermost link.** Every link was decided against the
page grid, so a deeper one carries a slot several pages on and a target somewhere down the document.
`RestatedInTheNextColumn` now walks the chain: every link's slot becomes the container's own, the
outermost break-before keeps the band top, and every deeper target is **dropped** so (a) re-derives
it. Found only by running the 60-row case: the second column of the *resumed* page was being filled
at `ResumeTopOverride = 1200` — a §5.2 page-grid target from the previous column's fill — so the tail
of the flow landed on a third page while the flat control needed only two.

**(c) Which boxes a column holds is a prefix at every level, not only at the container's.**
`PlacedBelow` answers it for the container's own children and is the whole answer while a child is
atomic per column. New `BeyondThisColumn` reads the same three-way distinction off each link of the
chain — break-*before* means the box itself is beyond, break-*inside* means it is here up to where it
stopped and the walk descends into it, and everything after the boundary is beyond either way — and
both `DeepestBottomOf` and `BoxGeometrySnapshot.Capture` now skip those subtrees. That is what fixes
the double claim and the ghosts: the rejected row was captured into the column it was rejected from,
and the rows the fill never reached still carried the *measurement* pass's geometry, one tall virtual
column down the document. It is also what stopped the container reporting a height past its page,
since `DeepestBottomOf` was measuring the whole flow.

**Verified load-bearing by neutralizing each part separately**: (a) fails 5 of the new/existing
multicol tests, (b) fails 3, (c) fails 4. None of the three is redundant with the others.

**What was deliberately not done.** `column-fill: balance` treats a wrapper as one atomic child in
its packing estimate, so a nested fixture balances a little less evenly than the same rows as direct
children (14/10 against 12/12 on the 24-row fixture). That is the estimate's own approximation — it
is an estimate by construction, and the fill is correct either way — not a fragmentation defect.
A forced **page** break declared below the container's own child is still lost entirely; that is
[#395](https://github.com/jhaygood86/PeachPDF/issues/395), it has a different cause (the measurement
pass consumes the one-shot `_forcedBreakTop`), and its characterization test is unchanged by this.

**This closes what bounded #335's multicol half.** A `box-decoration-break: clone` box whose content
is blocks now has a column break inside it, and the room the word path already reserves is what the
fill obeys — measured on a 194pt band with a 12pt cloned bottom border, the first column stops one
20pt row earlier than `slice` does. The doc limitation naming that case is removed.

Tests: `MulticolLayoutIntegrationTests` (+8 — the break's placement on every affected box, the
every-word-claimed-exactly-once invariant across four fill/page-height combinations, the resumed
container's second column, a two-level chain landing inside its own containing block's padding, and
the `clone`/`slice` reservation difference). Full net8.0 suite green (6738), CLI suite green (96);
**100% diff coverage**; zero warnings on `dotnet build PeachPDF.slnx -t:Rebuild`. **68 of 69
showcases byte-identical** to `main` once `/CreationDate`, `/ID`, the font subset tag and the
annotation `/NM` **and `/M`** values are normalized — note `/M`, an annotation's modification
timestamp, which is not one of the four the earlier entries list and which makes five showcases
differ on every run if it is left alone. `multicol` differs, by its new section 12 alone: pages 1–6
rasterize identically and page 7 shows the wrapper filling one column and leaving the second empty
on the unfixed build against a proper two-column split with a fragment's decoration in each on the
fixed one. Verified in both PDFium and MuPDF.
