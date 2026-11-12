# A flex container with `direction: rtl` is mirrored

**#2336.** A `display:flex` row with `direction: rtl` was laid out left to right. CSS Flexbox 1 section 5.1: a row's main axis is the
inline axis of the container's writing mode, so main-start is the right edge for `rtl`; a column's cross axis is the inline
axis, so its cross-start is the right edge too. Chromium (`is_reverse_direction_` only for `*-reverse`; rtl through the
writing direction and a logical-to-physical mapping), Gecko (`FlexboxAxisTracker` holds the writing mode with its direction;
flow is rtl when `mIsMainAxisReversed != isRtl`) and WebKit (`mainAxisFlippedOffsetForRow`) all do it in one logical-to-physical
step.

**The seam already existed.** `ComputeAxisMapping` resolved main-start and cross-start from `LogicalPropertyResolver` with
`DirectionMode.Ltr` hard-coded (its comment said the engine had no rtl support), and everything downstream (`_effectiveMainStartIsAtMax`
for the item coordinates, `justify-content: left/right`, the cross offsets) already keyed off the resolved side. Passing the
container's own `Direction` was the whole fix; no second mapping was written.

**Evidence.** `FlexDirectionRtlTests` (rtl row, ltr unchanged, rtl `row-reverse`, `flex-end`, physical `left`, rtl column
`flex-start`, source order from the start side); six of seven fail without the change. Engine suite 19697 and PeachPDF.Tests 1925 pass; the
252-showcase SVG sweep is byte-identical (no showcase has an rtl flex container).

**Not done.** Grid `rtl` columns were not checked here. Wrapped rows with `wrap-reverse` under rtl and auto margins under rtl have
no dedicated test (they use the same mapping).
