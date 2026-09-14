# `::marker` marker-box layout is not implemented

`::marker` marker-box width/height/margin/padding (the CSS Lists Level 3 "marker box" layout model) is not implemented — the spec itself (§3.1.1) declares this layout "not fully defined" and restricts applicable properties to `content`/`color`/font properties/`direction` (all of which PeachPDF fully supports on `::marker`, including real per-item numbering via the `list-item` counter and `<ol start>`/`<ol reversed>`/`<li value>`) — no browser implements marker-box sizing either, for the same reason. See [Pseudo-elements](docs/html-css-support.md#pseudo-elements).

**Vertical alignment is no longer part of this gap.** An `outside` marker now sits on the baseline of
its item's first line and contributes to that line's height, matching browsers — see
[`.claude/recent-fixes/2026-09-14-a-line-box-shares-one-baseline.md`](../recent-fixes/2026-09-14-a-line-box-shares-one-baseline.md).
What remains unimplemented here is the box model: width, height, margin and padding on `::marker`.
The one placement case still outside it is an item whose content is **block-level**
(`<li><p>…</p></li>`), whose marker keeps the item's content-box top rather than consulting a
descendant block's line boxes — deliberately, because a column-fill attempt the item is later
abandoned by leaves stale line boxes behind in it, and a marker positioned against one of those is
claimed by no fragmentainer and painted on no page at all
(`StraddlingListMarkerTests.ABlockContentItemAColumnFillAttemptAbandons_ClaimsItsMarkerExactlyOnce`
states this and fails on the wider walk). The two placements coincide unless the marker's font
differs from its item's.
