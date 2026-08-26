# A vertical `nowrap` run of more than one word can split across column breaks

_Tracked as [#844](https://github.com/jhaygood86/PeachPDF/issues/844). Found while working on
[#843](https://github.com/jhaygood86/PeachPDF/issues/843)._

`CssLayoutEngine.FlowBox` (horizontal) keeps a nested `white-space: nowrap` inline run's words
together via `wrapNoWrapBox`, moving the whole run to a fresh line as a unit if it doesn't fit
(see [#841](https://github.com/jhaygood86/PeachPDF/issues/841)). `CreateVerticalLineBoxes`, the
vertical-writing-mode counterpart that builds columns instead of lines, has no equivalent
mechanism - confirmed empirically: a `<span style="white-space:nowrap">first second</span>` inside
a `writing-mode: vertical-rl` box, with a column height too short for both words together, splits
the two words onto separate columns instead of moving the whole run together.

This also means `ApplyVerticalJustifyAlignment`'s own overflow-guard fix (#843 - flooring each
overflowing gap at the word's own natural spacing rather than a flat zero, and only hard-flushing
the last word when the column has exactly one word or isn't overflowing) currently has no way to
be exercised end-to-end through real markup: the scenario it protects against (two-or-more words
sharing one overflowing non-last column) can't be constructed until this gap closes, since normal
column-breaking always finds a split point between words once nothing forces them to stay
together. The fix's logic is otherwise identical to `ApplyJustifyAlignment`'s already-tested
horizontal counterpart (#840), just ported from `Left`/`Right`/`Width` to `Top`/`Bottom`/`Height`.
