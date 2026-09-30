# Content the fragment emitter dropped at page breaks is drawn

**Before (v0.9.20):** in documents that break across several pages, whole lines, headings or paragraphs
could be missing from the PDF although layout placed them: the fragment emitter concluded a box had nothing
left to show and skipped it on the pages after. Text inside floats, content inside boxes kept whole across a
break, and flex items inside a box that breaks were the common cases. On a corpus of fuzz documents about a
fifth lost some text this way.

**Now:** that content is drawn. Documents that were affected gain the missing text, and may gain pages;
documents that were not affected are unchanged.

**Why:** the emitter judged "this box has finished" from values that are not geometry: an inline box's own
bottom, which the inline flow never sets, and the bottom of a box whose height had not been applied yet.

Confirmed against `v0.9.20`: `FragmentEmitter.CommitGeometricallySettledObservations` compared
`box.ActualBottom` with the slot's top for every box, and `CommitRemainingObservations(bool commit)` marked
every box a pass found empty without any geometric check.
