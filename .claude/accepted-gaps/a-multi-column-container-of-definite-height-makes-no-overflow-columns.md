# A multi-column container makes no overflow columns on a paged surface

On a **continuous** surface (screen media) content that does not fit the columns of a container makes overflow columns to its right (css-multicol-1 section 3.3), as Chrome does: more forced column breaks than columns, and content past a definite `height` that does not clip. It is only those two causes: content that is merely taller than the surface's page band still goes on below, as it always has, and a container inside an outer column hands the rest to the next outer column.

On a **paged** surface (print, PDF) the same content continues on the next page instead of in columns beside the last one. Measured when overflow columns were tried everywhere: a document whose container spans pages (`FlexContainerInsideAMultiColumnContainer_FillsItsColumnOnEveryPage`) lost its later pages' columns off the page's right edge, so the page-continuation behavior stays for paged media. css-multicol-1 section 3.3 allows either on paged media (a fragmentainer break takes precedence over overflow), which is why this is a choice and not a deviation; a container that clips (`overflow` other than `visible`) drops the rest (#1818).

Where: `CssLayoutEngineColumns.MakesOverflowColumns` and `EndsAtAForcedColumnBreak`.
