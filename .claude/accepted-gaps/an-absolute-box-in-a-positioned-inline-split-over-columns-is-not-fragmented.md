# An absolute box whose containing block is a positioned inline split over columns is one box, not one per column (#2443)

The containing block of an absolute box inside a positioned inline is formed from the inline's first and last fragments
(`DomUtils.InlineContainingBlockOf`; css-position-3 section 2.1). When a multi-column container splits that inline over its columns, the
first fragment is in the first column and the last in the last, so the box is laid out once against the rectangle spanning all of them. The
specs fragment an out-of-flow box with its containing block: one fragment per column, each placed against the inline's fragments in that
column (first to last within it). Chrome draws one stripe per column for `inset: 0`.

Seen in the css-position `position-absolute-in-inline-007a/b`, `-008a/b` and `-009` to `-012` tests (horizontal and vertical-writing-mode
columns): the text already matches the references, only the box differs. Closing it means giving an absolute box a fragment per fragmentainer
of its containing block, which the fragment tree and `CssLayoutEngineColumns` do not model for an out-of-flow box today
(its page-break resume does, for a block, but not a per-column re-placement against a different containing-block rectangle).
Before that, nothing narrower is worth doing: any single rectangle is wrong for every column but one.
