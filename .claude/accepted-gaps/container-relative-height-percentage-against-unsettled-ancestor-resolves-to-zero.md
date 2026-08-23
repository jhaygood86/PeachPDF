# `cqh`/`cqb`/`cqi` still resolve to 0 for a percentage `height` against a not-yet-settled ancestor

Tracking issue: [#807](https://github.com/jhaygood86/PeachPDF/issues/807). A narrower residual of
[#805](https://github.com/jhaygood86/PeachPDF/issues/805) (closed) — see
[.claude/recent-fixes/2026-08-23-vi-vb-cqi-cqb-follow-writing-mode.md](../recent-fixes/2026-08-23-vi-vb-cqi-cqb-follow-writing-mode.md)
for that fix's own reasoning.

`CssBox.GetContainerRelativeUnitBasis` reads a `container-type: size` container's own physical height live,
off `ClientBottom - ClientTop`, at the moment a descendant's own width is resolved (top-down, during the
container's own child-layout phase) — before `CssLayoutEngine.ApplyHeight` (which settles `ClientBottom`)
runs in that container's own layout epilogue. #805 closed this for the common case — a container with a
**definite, explicit-length** `height` (e.g. `height: 200px`) — by resolving that value directly from the
`Height` CSS string (`CssBox.ResolveDefiniteHeightPt`) instead, since a definite length never depends on
content and is always safe to resolve early.

A **percentage** `height` (e.g. `height: 50%`) is different: resolving it needs the containing block's own
height to already be known (`PercentageBase(box).IsHeightCalculated`), and that ancestor's own height can
be subject to the exact same not-yet-settled timing gap, recursively. `ResolveDefiniteHeightPt` correctly
detects this (via `CssLayoutEngine.GetBoxHeight`'s own percentage gate) and returns `null` rather than a
wrong number, but its caller then falls back to the live `ClientBottom` read — which is `0` for the same
reason #805 existed. So `cqh`/`cqb`/`cqi` against a percentage-height `size` container can still resolve to
`0` today, in exactly the scenario `.claude/migration-notes/2026-08-23-vi-vb-cqi-cqb-follow-writing-mode.md`
does **not** claim to have fixed.

Closing this fully means recursively resolving an arbitrary ancestor chain's own (possibly also
percentage, possibly also not-yet-settled) height early, before that ancestor's own children are laid
out — a materially bigger, riskier change than resolving one box's own already-known-definite length. See
[.claude/invariants/fragmentation-a-boxs-own-measurements-are-only-valid-at-specific-times.md](../invariants/fragmentation-a-boxs-own-measurements-are-only-valid-at-specific-times.md)
for why a box's measurements being valid only at specific times is treated as load-bearing elsewhere in
this engine, not something to casually work around a second time.

The reader-facing note is in `docs/html-css-support.md`'s CSS Container Queries section, `cqh`/`cqb` rows.
