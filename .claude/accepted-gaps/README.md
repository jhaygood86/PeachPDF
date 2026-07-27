# Out of scope / accepted gaps

One file per gap, named for the gap rather than for a date. **Don't relitigate one of these without
new information** — each records a limitation already argued through once, and several record an
approach that was tried, measured and rejected. Read the relevant file before "fixing" behaviour it
describes; what looks like a defect may be a decision with a reason attached.

**These do not expire.** Unlike [.claude/recent-fixes/](../recent-fixes/), a gap file is deleted only
when the gap itself is closed — and closing one means deleting the file *and* removing the matching
note from the user-facing page in `docs/**` that describes the limitation. A gap that is genuinely a
spec deviation must also be tracked as a GitHub issue, referenced from the file here; see
[CLAUDE.md](../../CLAUDE.md#post-change-review-pass).

## Index


- [`::marker` marker-box layout is not implemented](marker-box-layout.md)
- [`letter-spacing` at the start/end of a line](letter-spacing-line-boundary-exemption.md)
- [Acid2's checkerboard-interlock band at the shared clip edge](acid2-checkerboard-interlock-band.md)
- [Acid2's fixed bars repeat onto a page `.intro` does not cover](acid2-fixed-bars-repeat.md)
- [Content-empty page slots are not materialized](content-empty-page-slots.md)
- [em/rem-relative `calc()` in an SVG length uses a fixed 16px approximation](svg-calc-em-rem-approximation.md)
- [Interaction-state pseudo-classes never match](interaction-state-pseudo-classes.md)
- [Margin collapsing ignores clearance in the adjoining set](margin-collapsing-ignores-clearance.md)
- [Margins adjoining an unforced break are truncated — two remaining gaps](margin-truncation-remaining-gaps.md)
- [Named-page activation and reversion outside normal block flow](named-page-reversion-outside-block-flow.md)
- [No text shaping](no-text-shaping.md)
- [Paint order for a float hoisted past a plain, non-positioned wrapper](paint-order-hoisted-float-plain-wrapper.md)
- [Per-character font matching: remaining coverage boundaries](per-character-font-matching-boundaries.md)
- [Per-page horizontal reflow is scoped to auto-width main-column blocks](per-page-horizontal-reflow-scope.md)
- [SVG features that are out of scope entirely](svg-features-out-of-scope.md)
- [SVG outlined-text / `<textPath>` residual subset](svg-outlined-text-textpath-residuals.md)
- [SVG text §10.4 per-character positioning edge cases](svg-text-per-character-positioning.md)
- [Tagged PDF: anonymous table structure cannot have its tagging overridden](tagged-pdf-anonymous-table-structure.md)
- [The table engine's whole-table pre-checks decide from an estimate](table-pre-checks-decide-from-an-estimate.md)
- [Tiled background layers need nearest-neighbour scaling to interlock](no-image-rendering-pixelated.md)
