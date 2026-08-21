# Vertical writing-mode: floats, positioning, hyphenation, bidi, and text-align now work

Content laid out via real vertical line flow (a `writing-mode: vertical-rl`/`vertical-lr` box holding
plain inline content) previously silently ignored five properties that already worked in ordinary
`horizontal-tb` flow: `float`/`clear`, `position: absolute`/`fixed`, `hyphens: auto`/manual soft-hyphen
breaks, Unicode Bidi Algorithm reordering, and `text-align`. All five now work.

- **`text-align`** (`left`/`right`/`center`/`justify`/`start`/`end`) now repositions a column's words
  along the inline axis instead of always packing flush to the inline-start edge. Under
  `direction: rtl`, natural (unstyled) placement already sits flush at the physical **bottom** edge —
  the opposite of `direction: ltr`'s physical-top default — so which keyword is a visual no-op flips
  between the two directions.
- **Bidi reordering** now runs for vertical content: an embedded RTL run inside LTR vertical text (or
  vice versa) reorders and mirrors correctly, matching the horizontal engine's existing behavior.
- **`hyphens: auto`/manual** now split an overlong word across a column boundary with a hyphen, instead
  of leaving the whole word to overflow (or, previously, never being attempted at all for the very
  first word of a column even in horizontal-equivalent cases).
- **`position: absolute`/`fixed`** content nested inside vertical inline text now resolves its
  `left`/`top`/`right`/`bottom` fully against its nearest positioned ancestor (or the page, for `fixed`)
  and reserves no space in the surrounding column flow — previously it flowed as ordinary inline
  content, ignoring its own positioning entirely.
- **`float: left`/`right`** on a sibling of vertical text now actually narrows the columns that share
  its physical position — vertical columns wrap around a float the same way horizontal lines already
  do. `float`'s own physical-left/right semantics are unchanged by writing-mode (confirmed against
  current spec text and real browser behavior); only the *column-avoidance* side of float layout was
  missing before.

**Not part of this change**, and still open as separate, narrower issues: a float's own starting
position when interleaved between vertical text runs, and `clear` on an ordinary block-level child of
a vertical box's block content ([#796](https://github.com/jhaygood86/PeachPDF/issues/796)); an RTL
auto-height inline-only vertical box positioning its words outside its own final bottom edge
([#797](https://github.com/jhaygood86/PeachPDF/issues/797), a pre-existing bug independent of this
change); and an absolutely/fixed-positioned descendant with an auto width having its position corrupted
by the box's own auto-width shrink logic ([#798](https://github.com/jhaygood86/PeachPDF/issues/798), also
pre-existing).
