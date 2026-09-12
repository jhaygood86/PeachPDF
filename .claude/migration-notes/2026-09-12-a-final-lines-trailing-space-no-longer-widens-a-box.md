# A final line's trailing space no longer widens a shrink-to-fit box

**Before (v0.9.18 and earlier):** the white space that ended a block's own final line was counted in
its max-content width. Only a `<br>` hung it. So `<div style="font:16px monospace">AB </div>` measured
19.7930pt while drawing 13.1953pt, and every shrink-to-fit consumer of that measurement — a float, an
inline-block, an auto table column, a flex item — came out one space wider than its content. Because
source indentation collapses to a space, `<div>\n  AB\n</div>` had this as readily as a literal
trailing space did.

**Now:** the trailing white space hangs, per
[css-text-3 §4.1.2](https://www.w3.org/TR/css-text-3/#white-space-phase-2). Both forms measure
13.1953pt — the same width Chromium gives (17.5938px).

**Why:** the rule was applied at a `<br>` only, which was the one point the intrinsic walk knew a line
had ended; it now applies at the block boundary and at the end of the walk too (issue #1014). A
second, separate measurement — the one that decides a float's used width — was counting the same
space and no longer does.

**Unchanged, deliberately:**

- A space **between** two inline boxes is a real inter-word gap, not a hanging space:
  `<span>AB </span><span>CD</span>` still measures the full 32.9883pt.
- `&nbsp;` is not white space for this rule and is still counted in full.
- A `white-space: pre` / `pre-wrap` trailing space is preserved content, not a collapsible gap, and is
  still counted in full.

**What a document author may notice:** a float, inline-block, auto-sized table column or flex item
whose content ends in white space (including ordinary source indentation before the closing tag) is
now up to one space narrower, and content beside it may shift in by the same amount. Where a document
relied on that space for separation, add the `margin`, `padding` or `&nbsp;` it actually wanted. Text
is unaffected — it always drew at the narrower width; only the box around it was wide.
