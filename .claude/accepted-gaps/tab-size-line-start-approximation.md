# `tab-size` measures stops from the last explicit newline, not the true rendered line start

Per CSS Text 4 §3.6, a tab stop is a multiple of `tab-size` measured from the starting content edge of
the preserved tab's nearest block container ancestor - i.e. the true start of the line the tab is
actually rendered on. `CssBox.MeasureWordsSize`/`CssBox.ExpandTabs` approximate that as "the most recent
explicit line break (`\n`) within this box's own `Words` list", tracked via a running `lineX` offset that
resets at each `\n` word.

This is exact for `white-space: pre` (which never soft-wraps, so every rendered line boundary really is
an explicit `\n`) and for the common case this property exists for - a tab indenting the start of a line
of preformatted/source-code content. It's an approximation, not the real thing, in two narrower cases:

1. A soft wrap under `white-space: pre-wrap` - stops after the wrap point still measure from the last
   explicit `\n`, not the actual wrapped-line start.
2. A tab preceded by content from a *sibling* inline box on the same rendered line (e.g.
   `<pre>foo<em>bar</em>\tbaz</pre>`) - `CssBox.Words` belongs to one box, so each inline box's own
   `lineX` starts fresh at 0, ignoring a preceding sibling's width.

A fully correct fix needs the tab's position on its actual final rendered line, which for soft-wrapped or
multi-box content is only known once line-breaking has happened - it would move tab-stop resolution into
`CssLayoutEngine`'s line-building pass (where each word's real `Left` is already computed sequentially)
rather than resolving it earlier, per-box, in `MeasureWordsSize`. Tracked as
[#885](https://github.com/jhaygood86/PeachPDF/issues/885).
