# An absolute box in a positioned inline split over columns is sliced per column (#2443)

The box used to be laid out once against one rectangle (first fragment to last, across every column). The load-bearing idea:
the containing block is formed **as if the columns were one strip** - inline edges are the start-most edge of the first
fragment and end-most of the last (swapped in rtl), each measured from the inline start of *its own* column, and the same
offsets apply in every column; the strip is cut at the column bands. WPT's reference confirms it (the second column's slice
starts at the same offset as the first, not at the first line of that column), which is not what the issue text says ("first
to last fragment within the column").

Mechanics: after a fill settles, `RestateFragmentedAbsolutes` lays the box out once per column with
`CssBox.FragmentContainingBlock` set on the inline (read first by `DomUtils.InlineContainingBlockOf`) and states each layout
into that column's captured snapshot (`BoxGeometrySnapshot.AddSlice`, flagged `IsStatedPerFragmentainer`); the emitter's
"only the last column holding an absolute box emits it" rule yields to that flag.

Traps found by running it: a fragment's rectangle is the font's content area and can reach above the line box and above the
column band, so column membership uses the rectangle's centre, not its top; the engine leaves a zero-width fragment of the
inline at the start of the line after a trailing `<br>`, which browsers do not count, so empty fragments are ignored while the
inline has any other; `FirstHostingLineBox`/`LastHostingLineBox` are not reliable across columns, so first/last come from
column order and position.

Evidence: WPT css-position +3 (007a, 009, 011; ltr and rtl), no change in any other module; `FragmentedInlineContainingBlockTests`.
Not done: vertical writing modes and block-in-inline splits (see the accepted gap file).
