# `break-before`/`break-after: always` is now rejected as invalid

**Landed:** 2026-07-25 (82c584ba) — Correct the break-* rationale, and state the forward-compatibility policy
**Doc section:** docs/html-css-support.md § [Page Breaks](../../docs/html-css-support.md#page-breaks)
**Verified against v0.9.6:** this note was not present in the `v0.9.6` tag's docs — confirmed genuine behavior change since 0.9.6, in scope for the next release notes.

`break-before: always` and `break-after: always` were previously accepted and forced a page break. They are not part of the value set above, so they are now rejected as invalid and have no effect — see [Forward compatibility](#forward-compatibility). The standards-compliant spellings both still work: `break-before: page` (CSS Fragmentation Level 3) and `page-break-before: always` (the legacy alias, which continues to mean `page`).
