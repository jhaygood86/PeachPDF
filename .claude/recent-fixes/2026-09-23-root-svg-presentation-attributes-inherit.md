# Root `<svg>` presentation properties seed inheritance (#1276)

**Symptom:** a real page's header icons (search/cart, `background-image` data-URI SVGs of the form
`<svg viewBox=… fill="#fff"><path …/></svg>`) rendered black instead of white.

**Cause:** `SvgTreeBuilder.BuildDocument` built the root's children with `InheritedPaint.Initial`,
never running `ApplyCommon` over the root node itself. Only its `font-*` (via `ComputeFontContext`)
were inherited from the root; every paint property on the outermost `<svg>` was silently dropped.
Nested `<svg>` went through `BuildNestedSvg` → `ApplyCommon` and was fine, which is why no existing
test caught it (none put `fill`/`stroke` on the root).

**Fix:** `BuildDocument` resolves the root through `ApplyCommon` onto a throwaway `SvgGroupElement`
and passes the returned `InheritedPaint` to the children. Only the returned *inherited* values are
used — the root's non-inherited `opacity`/`transform`/`clip-path`/`mask`/`filter` are still not
applied (unchanged behaviour; the throwaway element is discarded).

The UTF-8 BOM and XML declaration in the reported data URI were red herrings — they already parsed.

**Evidence:** `SvgTreeBuilderTests.Fill_OnRootSvg*` and
`BackgroundImageSvgIntegrationTests.SvgUrlBackground_RootSvgFill_IsInheritedByPath` failed before,
pass after; full net8.0 suite green (13179 passed). The original page re-rasterized with PDFium and
MuPDF shows both icons white.
