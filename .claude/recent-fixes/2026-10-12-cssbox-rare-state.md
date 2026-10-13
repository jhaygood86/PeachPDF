# CssBox: the state only unusual boxes have lives in BoxRareState

A `CssBox` was 1,184 bytes: 167 instance fields, 1,166 bytes. Over five benchmark documents 128 of those fields (925 bytes) were
populated on under 6% of boxes: table collapsed-border state, forced-break and float bookkeeping, pseudo-element links, grid
item pins, logical-property declarations, section ids and so on. With `DerivedStyle` already slimmed, `CssBox` and its style
were three quarters of the live heap of the 174,000-box MathML benchmark.

**The change.** Those members moved into `BoxRareState`, reached through properties of the same names on `CssBox`: the getter
returns the default without allocating, and the setter allocates the state only for a value other than the default (a write
of the default to a box with none is a no-op). `CssBox` is 288 bytes. The private members kept their underscore names so
no call site moved. `Counters`, `NamedStrings` and `FinalizedCounterNames` were eager empty collections per box; they are
allocated on first write now, and `TryGetCounter`/`GetCounterOrNull`/`HasNamedStrings`/`NamedStringValues`/
`TryGetNamedString` read without allocating.

**Parse and per-box dictionaries.** `ParseHtmlTag` built each element's attributes with `GroupBy` + `ToDictionary`, and the `HtmlTag`
constructor then copied that dictionary again into a case-insensitive one: three dictionaries per tag, and one for every tag with no
attributes. It now builds the case-insensitive dictionary once (first duplicate wins), skips it for an attribute-less tag
(`HtmlTag.Attributes` is already nullable), and the constructor only copies a caller's dictionary that is not already in that
form. `CssBox.Rectangles` (an 80-byte dictionary on every box, used only by inline boxes) is created on first write; the readers
every box goes through use `RectangleCount`/`RectangleKeys`/`RectangleValues`/`RectanglesView`, which look without creating it.
A shared empty `Rectangles` instance was tried first and abandoned: one test wrote through `box.Rectangles[...]`, and a write to a
shared instance would corrupt every box, so the property allocates and the hot readers opt out.

**Trap that cost a round.** Choosing "rare" by what a box holds *after* layout is wrong: a field written non-default during
layout and reset afterwards looks unpopulated, but its write still allocated the 900-byte state. `_canRestartChildLoop`
(set true on entry to every block's children, false on exit), `_incomingToken`, `_liveChildStartIndex`, `_resumeTopOverride`,
`_orphansBreakTaken`, `_widowsRewindTaken`, `_resumingIntoTheNextFragmentainer`, `RequestedBreakBefore*`,
`_groupBlockStartMarginOverride`, `_awaitingRefill`, `PositionAssignedByEngine`, the quote counters, `PendingBreakToken` and
the `::before`/`::after` flags all looked rare and were not. They stay as plain fields. Count the non-default *writes* per
field (and note the allocation counter only sees the first field to allocate), not the end state, before moving a field.
A new field written on a common path belongs inline.

**Measured** (warm, in-process): math-l allocation 1,059 -> 892 MB per render, GC heap ~490 -> ~326 MB, GC pause ~790 -> ~600 ms,
wall ~2.1 -> ~1.8 s; table-m heap 240 -> 208 MB. Whole-session total for math-l: 2.6 s / 1,469 MB -> ~1.8 s / ~890 MB.

**Evidence.** Release suite green; PyMuPDF words+drawings identical to main on 19 benchmark documents, fuzz documents and all
220 showcase PDFs; 0-warning rebuild.
