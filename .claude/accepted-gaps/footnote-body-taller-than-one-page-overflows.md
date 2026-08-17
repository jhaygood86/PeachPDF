# A footnote body taller than one page's content band overflows rather than splitting

`Dom/FootnoteBodyLayout.LayoutFootnoteBodyFor` forwards to `RunningElementLayout.LayoutRunningElementFor`,
which lays its subject out with `HtmlContainerInt.SuppressWordPageBreaks = true` and a detached
fragmentainer - the same "detached, breaking-suppressed, single whole pass" treatment css-gcpm-3's
`content: element()` running-element content already gets. A footnote body's content is therefore always
monolithic: it never fragments across pages, even when it is taller than the page it landed on. Its
content simply overflows the reserved note area rather than continuing onto a later page.

This mirrors the well-established "content taller than a whole band overflows rather than moving forever"
behavior this engine already applies to ordinary monolithic content ([css-break-3 §2](https://www.w3.org/TR/css-break-3/#monolithic)),
and to running elements specifically. Splitting a footnote body across pages - which real print engines
generally do support - would need the note area itself to become a genuine fragmentation context with its
own resumable layout pass, coupled back into the very page-break decision its own height already
influences; a materially larger change than this version's scope.

Filed as [issue #752](https://github.com/jhaygood86/PeachPDF/issues/752).
