# `HasStackingHoistCandidates` (and the float/out-of-flow flags beside it) now see into a repeated header/footer's proxy

`HtmlContainerInt.ComputeFlowFlags` computes three document-wide flags — `HasFloatedBoxes`,
`HasOutOfFlowBoxes`, `HasStackingHoistCandidates` — by walking `CssBox.Boxes` from `Root` once, after
layout. `StackingOrder.Flatten` gates its entire hoisting search on `HasStackingHoistCandidates` alone
(`if (!(box.HtmlContainer?.HasStackingHoistCandidates ?? true)) yield break;`): a plain-wrapper ancestor's
ordinary paint loop deliberately skips a descendant that needs hoisting, trusting some enclosing search to
find and paint it instead. When the flag is wrongly `false`, no such search ever runs, so the box is not
merely mis-clipped or mis-ordered — it is **never painted on any page**.

**The gap.** A repeating `<thead>`/`<tfoot>` is detached from the live tree
(`CssLayoutEngineTable.RemoveHeaderFooterFromTree`) and stood back in per page by a `CssProxyBox`, whose
real content lives in `SourceBox` — deliberately not part of `.Boxes`, exactly so one source subtree can
be shown at many positions (see `CssProxyBox`'s own remarks, and `FragmentEmitter.ChildrenOf`'s identical
unwrap for fragment-building). `ComputeFlowFlags`'s walk had no equivalent unwrap, so a stacking-context
box (`position:relative;z-index:0`, `opacity:0.99`, etc.) reachable *only* through a repeated header's
source subtree was invisible to the scan. With no other stacking-hoist candidate anywhere else in the
document, `HasStackingHoistCandidates` came out `false` and the box's content silently never painted at
all, on any page — reproducible on a single page, no pagination needed.

**Fix.** `ComputeFlowFlags`'s recursive step now also descends into `((CssProxyBox)box).SourceBox` when
the box being visited is a proxy, mirroring `FragmentEmitter.ChildrenOf`'s own proxy unwrap. Applied to
all three flags uniformly, not just the stacking one — a float or an out-of-flow box that exists only
inside a repeated header/footer would have hit the identical blind spot in `DomUtils.GetFirstIntersectingFloatBox`'s
own `HasFloatedBoxes` short-circuit, just with a less visible symptom (a float not avoided, rather than
content dropped outright).

**Found, not invented, while working on a *different* issue** (#345, `RenderUtils.PushAncestorOverflowClips`
reading live `CssBox` geometry instead of the fragment tree) — the natural end-to-end repro for #345 needed
a stacking-context box inside a repeated header, which turned out to not paint at all, independent of
whatever #345 itself fixes about clip geometry. The two are unrelated: this is about whether a stacking
participant is *found*, #345 is about the clip rect once one *is* found.

**Evidence.** New tests in
`src/PeachPDF.Tests/Integration/RepeatedTableHeaderClipIntegrationTests.cs` —
`StackingContextOnlyReachableThroughARepeatedHeader_IsPaintedOnEveryPage` (fails on every page pre-fix:
`HEADERMARKER` never appears in the paint recording) and a plain, non-table control
(`StackingContextInsideAPlainOverflowClip_IsPainted`, passes unchanged before and after — confirming the
gap is specifically about the proxy, not stacking hoist in general). Full suite (net8.0, 9920 tests) and a
solution-wide `dotnet build -t:Rebuild` both clean; diff coverage 100% on the changed lines.
