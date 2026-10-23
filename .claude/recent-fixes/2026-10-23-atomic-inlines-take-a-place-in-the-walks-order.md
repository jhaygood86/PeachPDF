# A run of atomic inlines paginates: each takes a place in the walk's order (#1821)

About a thousand `display: inline-table` tables in one block never finished laying out on an A4 page (500 took 1 s,
750 under 1 s, 1000 never). Not slow: an endless loop of passes, each costing more than the last.

**How it was found.** Sampling the hung CLI with `dotnet-trace` (`--providers Microsoft-DotNETCore-SampleProfiler`,
convert to speedscope, aggregate by frame) put 91 % of the time in `FragmentEmitter.CatchUpStaleSlotsBehind`: not
layout at all, but the emitter re-walking every page of an ever-growing list. The container's own counters then showed
the cause (`FragmentainerPasses` climbing past 150 for 120 tables, with only about 20 left to place). Placing the tables
after the break ended up on page 8 after 8 passes: each pass laid out all of them again from the top of the next page.
The same document with a word between the tables (`</table> a <table`) finished in 0.7 s, and so did a taller page
(no break), which isolates it to a break taken between two atomic inlines.

**Cause.** The break token resumes at a word ordinal (`CssLineBoxCoordinates.WordOrdinal`), and only words advanced
it. An inline-table, inline-grid, inline-flex or block-content inline-block lays its content out in its own formatting
context and takes no word, so a run with no word between them all sit at one ordinal. The break recorded that ordinal
as the resume point; the next pass found `childOpensHere` true for every box of the run and placed them all again, so
the box that straddled the foot straddled again on the next page, for ever (until `MaxFragmentainers`).

**Fix.** Each such atomic inline now takes one ordinal, whether the pass places it or only walks past it
(`CssLayoutEngine.FlowBox`: after `FlowAtomicBlockContentChild`, after `FlowInlineFlexChild`, and in both
`if (!childOpensHere)` skips). A line that opens with an atomic records that atomic's own ordinal, so the resumed pass
skips the ones before it. Ordinals are only positions in the walk's order (nothing indexes `Words` by one), so a number
that means the same on every pass is all that is needed; the comment on the inline-flex skip that said an inline-flex
"contributes no word ordinals" is reworded.

**Not fixed, same shape.** A float also takes no ordinal; a run of floats with no word between them and a break
between two of them has the same ambiguity, though floats are placed by `FlowFloatChild` on the pass that reaches them
rather than re-placed, so no document has been seen to loop.

**Evidence.** `AtomicInlineRunPaginationTests`: 400 tables/grids/flex boxes over several 300x200pt pages with each
separator that adds no word, plus 1000 tables on A4; each asserts every box has a place of its own, document order, and
passes bounded by pages. Full `PeachBrackets.Engine.Tests` and `PeachPDF.Tests` suites.
