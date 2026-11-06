# SMIL animation and live SVG animation

**Before.** `<animate>`, `<animateTransform>`, `<animateMotion>`, `<set>` and `<animateColor>` inside an inline `<svg>` were ignored in every mode, and a screen host that played the
document did not play the CSS animations of the elements of an `<svg>` either (only a still frame, `AnimationProgress`, sampled those).

**Now.** A document played on a clock (`HtmlDocumentOptions.AnimationsEnabled`) plays both, and `AnimationProgress` samples SMIL at that fraction of the span the animations cover (the end of the
longest one's first interval). Without either, SMIL is still ignored, so a PDF made as before looks as before.
