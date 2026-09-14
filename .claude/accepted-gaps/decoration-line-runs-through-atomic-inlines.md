# A decoration line runs through atomic inlines instead of breaking around them

[css-text-decor-3 §2.4](https://www.w3.org/TR/css-text-decor-3/#line-decoration) is unambiguous:
"Atomic inlines, such as images and inline blocks, are not decorated." PeachPDF draws the line
straight through them. `FragmentPainter.CollectDecorationSpans` unions every line-hosted
descendant's rectangle into one span per `CssLineBox`, and an atomic inline is line-hosted like any
other box, so its rectangle widens the span rather than cutting it.

**Do not conclude from a rendered page that this is already correct.** The deviation is nearly
invisible: the common atomic inlines paint opaque content exactly where the line wrongly is, so an
image or a background-carrying inline-block covers it and a rasterized check shows the gap the spec
wants for the wrong reason. Giving the atomic inline `opacity: 0` and reading the raw content stream
is what exposes it — `<div style="text-decoration:underline">AA <span style="display:inline-block;
width:60pt;opacity:0">hidden</span> BB</div>` emits one unbroken 95.5pt segment covering `"AA "`, the
whole 60pt inline-block and `" BB"`, where the spec wants two segments with a 60pt gap.

Pre-existing, and older than the propagation mechanism that now collects the spans: before
[the block-propagation fix](../recent-fixes/2026-09-14-block-text-decoration-propagates-to-inline-content.md),
a block's own full-width decoration rectangle ran through the atomic inline in exactly the same way.
That change inherited the deviation rather than introducing it, and deliberately did not widen its
own scope to fix it.

Fixing it means *subtracting* an atomic inline from a span, so one decoration line becomes several
drawn segments — the same shape `text-decoration-skip-ink`
([issue #1064](https://github.com/jhaygood86/PeachPDF/issues/1064)) needs, so the two belong on one
mechanism rather than two. Two details that will otherwise be re-derived: the exclusion is the atomic
inline's **margin** box, while the rectangles collected today are border boxes; and an atomic inline
must be recognized by its own display type, since an `inline-block` whose content is inlines-only
reaches paint through a different layout path (`CssLayoutEngine.FlowAtomicBlockContentChild` vs. the
ordinary inline path) than one whose content is block-level, and both must be excluded.

Filed as [issue #1066](https://github.com/jhaygood86/PeachPDF/issues/1066); the content-stream
evidence above was taken from PeachPDF's own output, cross-checked in PDFium and MuPDF.
