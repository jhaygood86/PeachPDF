# Inline boundaries no longer create wrap opportunities

## What was wrong

Horizontal and vertical inline layout treated every new `CssRectWord` owner as if a normal soft-wrap
opportunity preceded it. Markup alone could therefore split a token: `text<span>more</span>` wrapped
between `text` and `more` even though no whitespace or other line-break opportunity existed. With
`overflow-wrap: anywhere`, the whole span first moved to a fresh line and only then used emergency
breaks, leaving usable space behind. Emoji spans made the difference especially visible.

## The load-bearing idea

`HasOrdinaryWrapOpportunityBefore` now distinguishes parser-created opportunities (whitespace, a
hyphen, CJK character boundaries, and `word-break: break-all`) from a pure inline-owner boundary.
Whitespace-only DOM boxes between sibling inline elements are detected through the existing
first-child-chain predecessor walk because they intentionally carry no `CssRectWord`.

When no ordinary opportunity exists, `overflow-wrap` may split the current word against the space
remaining on the current line or vertical column. Without an emergency value, the adjacent runs stay
one unbreakable token and overflow together. Synthetic font/orientation/small-caps fragments remain
glued through `SuppressWrapBefore`.

An authored whitespace or punctuation opportunity retains priority over `overflow-wrap`'s emergency
opportunities. Emoji assigned Unicode's ideographic, emoji-base, or paired regional-indicator
line-break opportunities are different: Unicode permits an ordinary break between adjacent eligible
clusters. The tokenizer therefore emits one word per complete eligible emoji cluster, keeping
variation-selector, modifier, regional-indicator, tag, and ZWJ sequences intact. Emoji in other
line-break classes, such as keycaps, do not gain a break merely from being emoji. This lets the eligible
emoji fill each line normally, while an authored space before a following long Latin token moves that
token to a fresh line before emergency splitting begins.

Segoe UI Emoji exposed a separate measurement defect: it maps U+FE0F to a cmap glyph with a normal
advance. A variation selector modifies the preceding character and never owns an independent advance,
so the shaper now hides it after GSUB even when the font maps it. Without that rule `✌️` and `🕊️`
measured exactly twice as wide as every other emoji despite painting only one symbol.

The same decision also drives min-content sizing: adjacent normal/`break-word` fragments accumulate
as one unbreakable run, while `anywhere` still contributes its widest grapheme. If the remaining
measure cannot fit even one grapheme, the emergency opportunity at the transparent element boundary
moves that grapheme to a fresh line/column rather than overflowing the current one.

## Evidence

- Exact Windows regression against Calibri and Segoe UI Emoji matches Edge's emoji distribution and
  keeps the following long token off the final emoji line.
- Horizontal and vertical adjacent-inline tests, no-grapheme-fits and min-content tests, plus
  whitespace, hyphen, bidi, emoji-sequence, and per-codepoint font-fallback contrast tests.
- Full net8.0 suite green: 10,621 passed, 9 platform-specific skips. Diff coverage is 99% over 299
  changed production lines. Whole-solution rebuild completed with zero warnings and zero errors.
