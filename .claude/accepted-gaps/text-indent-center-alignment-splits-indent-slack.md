# `text-indent` doesn't fully compose with `text-align: center`

Tracking issue: [#623](https://github.com/jhaygood86/PeachPDF/issues/623).

While implementing `hanging`/`each-line` (issue #607) and making the base `text-indent` placement
direction-aware for RTL, a narrower gap was found and deliberately left out of that change's scope.

**`text-align: center` splits any indent-driven slack evenly across both sides.**
`CssLayoutEngine.ApplyCenterAlignment` shifts every word on the line by a uniform `diff / 2`. Since
`text-indent` is reserved as a gap on the line-start side (for LTR, via the flow-time `CurrentX`
offset; for RTL, via `FlowBox` narrowing the wrap boundary — see the `isRtl` handling in both
`CreateLineBoxes` and `FlowBox`), a subsequent center-alignment step halves that reserved gap and
puts half of it on the trailing side too — a centered, indented line ends up with roughly `indent / 2`
of extra space on the start side, not the full `indent`. This is a *different* bug shape from the one
#607 fixed for `ApplyRightAlignment`/`ApplyJustifyAlignment` (those put the *whole* reserved gap on
the wrong physical side under RTL — a "which edge" bug); `ApplyCenterAlignment` puts the right total
amount on each side by construction, but divides a start-side-only reservation in two (a "how much"
bug), regardless of direction.

**Deliberately out of scope for now.** Fixing it means `ApplyCenterAlignment` reserving the indent's
full amount on the line-start side rather than splitting `diff` evenly — a difference in what the
function computes, not just an RTL edge-case fix; see #623 for the write-up.

An RTL box with `text-align` explicitly forced against its own writing direction (e.g.
`direction: rtl; text-align: left`) was suspected to have the same "wrong side" problem #607's RTL fix
addressed for `right`/`justify`, but does not: `FlowBox`'s wrap-boundary narrowing for RTL (the
mechanism that landed to fix `ApplyRightAlignment`'s own indent-vs-diff interaction, see
`.claude/recent-fixes/2026-08-03-text-indent-hanging-each-line-and-rtl-indent.md`) reserves the
indent's space during flow itself, before any alignment step runs at all — so it already indents from
the physical right correctly under `text-align: left` too, with no further change needed. Verified
(`direction:rtl; text-align:left; text-indent:40pt`, Arabic text, `width:150pt`): the line's content
sits flush at the physical left as `text-align: left` requires, with a `44.17pt` gap to the physical
right — comfortably at least the `40pt` indent, the rest being ordinary wrap raggedness.
