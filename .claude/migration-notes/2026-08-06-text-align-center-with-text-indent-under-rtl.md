# `text-align: center` now reserves the full `text-indent` on the line-start side under RTL

**Landed:** 2026-08-06 — Fix open alignment issues (#623)
**Doc section:** docs/html-css-support.md § [text-indent row](../../docs/html-css-support.md#text)

`ApplyCenterAlignment` didn't inset its flush target for `direction: rtl`, unlike `ApplyRightAlignment`/
`ApplyJustifyAlignment` (both already RTL-aware since issue #607). For LTR content this was already
correct — the flow-time `CurrentX` offset that reserves the indent already excludes it from the slack
`ApplyCenterAlignment` splits evenly, so a centered LTR line already kept the full indent on its start
(physical-left) side (verified empirically: a 40pt indent measures as a full 40pt difference between the
left and right gaps either side of the centered content, both before and after this fix).

Under RTL, though, `text-indent`'s line-start side is the physical *right* — and because RTL reserves the
indent by narrowing the wrap boundary during flow rather than shifting the start position, nothing
excluded it from `ApplyCenterAlignment`'s own slack calculation. The indent was centered away to nothing:
a centered, RTL, indented line rendered symmetrically, with no visible extra space reserved on the
physical right at all.

A `direction: rtl` document combining `text-align: center` with `text-indent` will now see the indent's
full amount reserved as extra space on the physical right side of a centered line, with only the
remaining (non-indent) slack split evenly between the two sides — matching the LTR behavior that was
already correct.
