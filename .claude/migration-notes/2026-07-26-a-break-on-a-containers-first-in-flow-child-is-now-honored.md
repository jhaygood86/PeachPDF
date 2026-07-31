# A forced break on a container's first in-flow child is now honored

**Landed:** 2026-07-26 (8db4e425) — Document the first-child break point, and show one
**Doc section:** docs/html-css-support.md § [Page Breaks](../../docs/html-css-support.md#page-breaks)
**Verified against v0.9.6:** this note was not present in the `v0.9.6` tag's docs — confirmed genuine behavior change since 0.9.6, in scope for the next release notes.

A forced break declared on a container's **first** in-flow child — the common `section > h1 { break-before: page }` idiom — previously had no effect, and an oversized `margin-top` on such a child was never truncated. Both now behave as the spec describes, so a document using either gains page breaks, and possibly pages, it did not have before. A break on the very first thing in a document is unaffected: nothing precedes it, so it still produces no break and no blank leading page. See [Forward compatibility](#forward-compatibility).
