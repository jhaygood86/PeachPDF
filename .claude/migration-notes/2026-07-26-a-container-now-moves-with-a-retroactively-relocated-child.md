# A container now moves along with a retroactively relocated child

**Landed:** 2026-07-26 (c5e23e6a) — Move the container with the retroactive break movers too
**Doc section:** docs/html-css-support.md § [Page Breaks](../../docs/html-css-support.md#page-breaks)
**Verified against v0.9.6:** this note was not present in the `v0.9.6` tag's docs — confirmed genuine behavior change since 0.9.6, in scope for the next release notes.

The same is now true of a break that is *decided* rather than declared — `break-inside: avoid`, [monolithic content](#monolithic-content), or an `orphans` push relocating a container's first child. The container used to stay put and span the boundary, printing an empty copy of its own border, background and padding on the page its contents had just left; it now moves with them. Content following such a container shifts down a page correspondingly. See [Forward compatibility](#forward-compatibility).
