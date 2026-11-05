# Inline SVG style matching was redone for every property of every element

**Symptom (measured, Release build, arstechnica.com's front page in the WinUI demo).** Laying the page out took 5.7 s of a ~7.5 s load; the whole
network side (133 requests) took under 1 s. A `dotnet-trace` of the load put nearly all the managed time inside `CssBoxSvg.MeasureWordsSize` ->
`SvgTreeBuilder.Build` -> `ResolveStyledAttr` -> `CssData.GatherMatchedRules`, i.e. selector matching against the page's whole Tailwind sheet, for
129 inline icons.

**Cause.** `CssBoxSvgSourceNode.GetMatchedCssDeclarations()` ran a full match of every rule against the element on every call, and the tree builder
calls it once per property it reads per element (dozens), plus `IsMatchedCssDeclarationImportant`, which matched a second time. The comment on
`ResolveStyledAttr` ("re-parses ... per call rather than caching, matching this builder's existing preference for simplicity") is where the cost was
chosen; it was invisible until a page had hundreds of icons and thousands of rules.

**Fix.** The declarations (and the `!important` set) are kept per `CssBox` in a `ConditionalWeakTable`, valid for the same `CssData`, media, custom-property
context and `HtmlContainerInt.StyleGeneration`. Layout of the same page fell from 5.66 s to 1.55 s. **Trap for a change that restyles in place** (the
incremental interaction restyle): it keeps the boxes, so it must raise `StyleGeneration` or an icon whose rule reads `:hover` keeps its old declarations.
Document reloads build new boxes and are not affected.

**Other things found by measuring, not reading.**
- Debug vs Release did not matter for this (5.4 s vs 5.7 s): the cost was algorithmic. The Debug console traces are now behind `PEACHPDF_TRACE=1`
  anyway (they formatted a string per token/box/fragment even when nobody read them).
- Only 12 distinct font files served the page's 92 `@font-face` faces; 80 of 133 requests were repeat fetches of them, which the loader's cache now
  serves. `.NET` drops `Content-Length` from decompressed responses, so enabling `AutomaticDecompression` without caching length-less responses would have
  stopped every compressed response being cached; both changed together.
- A sampling `dotnet-trace` report ranks idle threads first; read the inclusive column and find the `CssBox`/`SvgTreeBuilder` frames.

**How to measure again.** The load pipeline reports `Activity` spans (`PeachBrackets` source, see docs/peachbrackets.md "Timing a load"). The WinUI demo
totals them in its status bar and, with `PEACH_TIMINGS_FILE=<path>`, writes every phase and every web request to that file when the document has loaded:
`$env:PEACH_TIMINGS_FILE='x.txt'; .\PeachBrackets.WinUI.Example.exe --showcase web_arstechnica`.

**Not done.** The remaining ~1.5 s of layout is largely image loading (0.9 s of it, inside layout); `css.cascade` is 0.7 s; font faces are still fetched
serially. Evidence: the suites (Engine 17,986, PeachBrackets 624, WinUI 221), the zero-warning rebuild, and the before/after timings above.
