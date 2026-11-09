# Content after a float that continues in the next column was placed a page back and drawn nowhere (#2290, #2291)

**Load-bearing idea.** A multi-column fill that resumes onto a float (the break token names the float, an out-of-flow
child) lays the float's continuation at the top of the column and then goes on to the in-flow children after it.
`CssBox.ResolveBlockChildOffset` derives a child's top from its previous in-flow sibling, and for the first in-flow child
after the float that sibling is the paragraph that ended in an earlier fragmentainer. The child was therefore laid out at
that paragraph's old bottom (in the #2290 document: document Y 93.5, on page 1, while the page the float continues on starts at 140), a page back; the column's snapshot held a geometry no
fragment claimed and the words were painted on no page. `LayoutBlockChildren` now gives that one child the column's
content top as a resume target (`_resumeTopOverride`), as it already does for a child the resume token names, so the
text is placed at the top of the column and avoids the float from there.

**Found by running it.** A tracing line in `CssLayoutEngineColumns.FillColumns` printing each child's `Location` after
every column fill showed it at once: the float at Y=140 (the page-2 top), the paragraph still at 93.5. A reduction from
the lost-text corpus (`columns: 2`, a float of ten words, a paragraph after it) reproduces it; it is not about the
words, only about the float outliving its first column.

**Evidence.** The three reductions (#2290, #2291, and a float followed by an `h1`) lose no word and draw none twice;
#2291's "last line drawn twice" is the same defect. Corpus (A: 300 documents, B: 100, A2/B2, same documents rendered by
`main` and by the branch): words lost 18,361 → 18,258 on A and 1,278 → 1,204 on B, duplicated 6,787 → 6,763 and
227 → 224; seven documents went from words lost to none (for example 59, 46, 34 and 27 words). Four documents lose one to four words more than on `main`
(`B_d1002` 83 → 87, `A_d217`, `A_d252`, `A2_d217`); they are documents that already lose dozens of words through
several floats at once, and the words they now lose are ones `main` happened to draw (cause not reduced; see [the residual losses](../accepted-gaps/some-documents-with-floats-still-lose-words-after-tall-floats-continue.md)).

**Boxes that are not in flow do not count.** A `display: none` box (or blank text) between the float and the paragraph is not "the first in-flow child": counting it gave the override to the hidden box and the paragraph after it was placed a page back again (a review found it; it has a test case).

**Not done.** Inline text or a `<span>` right after the float (the float and the text are then in one anonymous block, so the resume token names that block and not the column's children) still loses its words (#2312), and so does a second float after the first (the #2289 family). #2289 (a word after two side-by-side floats, one continuing onto the next page), #2294, #2295, #2296 are
other mechanisms and still lose or misplace a word.

**Tests.** `MulticolContentLossTests.ContentAfterAFloatThatContinuesInTheNextColumn_KeepsEveryWord` (three reductions,
each fails without the change) and, through `PdfGenerator` for the coverage gate,
`MulticolFloatContinuationPdfTests`.
