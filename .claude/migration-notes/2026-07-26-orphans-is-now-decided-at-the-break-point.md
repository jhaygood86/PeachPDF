# `orphans` is now decided at the break point, not by relocating after the fact

**Landed:** 2026-07-26 (079dd014) — §4.3's relaxation ladder, stated — and orphans decided at the break point (#372)
**Doc section:** docs/html-css-support.md § [Page Breaks](../../docs/html-css-support.md#page-breaks)
**Verified against v0.9.6:** this note was not present in the `v0.9.6` tag's docs — confirmed genuine behavior change since 0.9.6, in scope for the next release notes.

A box that would have left fewer lines than `orphans` behind used to be relocated *after* the fact (and only when it fitted one page); the break now falls before it instead, which both closes up the interior gap that relocation left inside it and brings boxes taller than a page into scope. See [Forward compatibility](#forward-compatibility).
