# "Did this cross out of its fragmentainer?" is one question with two bands

_Landed 2026-07-27._

**"Did this cross out of its fragmentainer?" is one question with two bands** ([issue #400](https://github.com/jhaygood86/PeachPDF/issues/400) (b), the second step of what [#390](https://github.com/jhaygood86/PeachPDF/issues/390) stage 3 is blocked on). `CssRect.WouldStraddleFragmentainer` had **two arms answering one thing**: a column arm comparing the word's bottom against the column's own `BandBottom`, and a page arm comparing *slot indices* (`SlotStartingAt(Top) < SlotEndingAt(Bottom)`). A slot index is a fact about the page grid, and a column has no slot of its own — so the two could not be the same expression while one of them was phrased in indices.

**The load-bearing identity is that bands are contiguous**: one band's bottom *is* the next one's top, on the uniform grid by construction and in `PageGeometryTable` by how it is built (`_pages[k].Top = _pages[k-1].Top + _pages[k-1].BandHeight`). That is what makes `PageIndexOf(x - eps) > k` and `x - eps >= PageBottomOf(k)` the same statement, so the index comparison can be rewritten as a coordinate one **exactly** rather than approximately — new `PageBand` + `HtmlContainerInt.FallsPast(bottom, band)`, and the derivation is pinned as a test (`FallsPast_AgreesWithTheSlotComparisonItReplaces`) rather than left in a comment.

**The two arms did genuinely disagree, at exactly one point**, which only became visible once they were written the same way: the page arm asked `>=` and the column arm `>`, so a bottom edge landing exactly one epsilon past the band bottom was a straddle on a page and not in a column. Both are defensible "within tolerance" answers and there is no principle to choose between them, so they are now the page arm's — which keeps the far commoner path byte-identical and moves the column path only at a measure-zero coordinate.

§5.2's boundary test in `CssBox.PlaceBlockChild` is converted the same way, and it is the more load-bearing of the two: it decides where a block-flow break falls. `naturalSlot > prevSlot` becomes `FallsPast(top, band)`, and `PageTopOf(prevSlot + 1)` becomes `band.Bottom` — the same coordinate, now named by the question that asked for it.

Behaviour-neutral: **all 65 showcases byte-identical**, full net8.0 suite green (6430), CLI green (96), **100% diff coverage**. Tests: 3 more in `HtmlContainerIntPageGridTests` (contiguity, the flush-on-the-bottom case, and the equivalence theory).

**Not done, and the next step in #400:** the page arm still names its band from the page grid rather than from the fragmentainer being filled, because a box being laid out is not guaranteed to be inside that one — monolithic content, the engines' measurement passes, and a box below a tall margin all put geometry outside the current band. Closing that is step (c), where a measurement pass names *no* fragmentainer instead of being given a suppressed one.
