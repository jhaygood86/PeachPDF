# A `display: none` sibling no longer sends a run of bare text down the block path, where it was never placed

`<style>p{margin:0}</style><div style="float:left;width:80pt">F</div>alpha bravo …` drew only the float on
v0.9.19 (and a run of blank pages when the text was long). Head content (`<style>`, `<meta>`, `<link>`,
`<head>…</head>`) with no `<body>` tag was the trigger; an explicit `<body>`, or wrapping the text in a `<p>`,
avoided it. `<style>…</style>alpha bravo` with no float dropped its text on **every** version back to 0.9.18.

## The load-bearing idea: three predicates ask one question, and one of them answered it differently

`HtmlParser.ParseDocument` builds no implied `<html>/<head>/<body>`: a fragment is one flat list under a
synthetic root, so the `display: none` `<style>` is a *sibling* of the text. Two of the predicates that decide
how such a run is flowed skip a `display: none` child (`DomParser.ContainsVariantBoxes` and
`ContainsInlinesOnlyDeep`, both since the 2026-08-11 display-none fix); the third,
`DomUtils.ContainsInlinesOnly` - the one `CssBox.LayoutContents` dispatches on - did not. `[style, #text]`
therefore failed the inlines-only test, took `LayoutBlockChildren`, and the bare text box reached the final
`else` of `LayoutContents`, which only copies the previous sibling's `Location`: no `FlowBox`, no
measurement, no placement, nothing painted.

It stayed hidden until #1205 because a float used to count as the "block half" of `ContainsVariantBoxes`, so
`float + text` was wrapped into an anonymous block whose own children *were* inline-only. #1205 stopped that
wrap on purpose (a float is neither half), which was correct, and exposed the predicate. Bisected to
095bdcc0; the same commit is why a multi-column container holding a float lost all its text (fixed separately
by #1344, now pinned in `MulticolFloatIntegrationTests`).

`ContainsInlinesOnly` now accepts `display: none`, and `FlowBox` steps over such a child. The second half was
found by running it, not by reading: with only the predicate changed, `alpha<span
style="display:none;margin:0 50pt;padding:0 50pt">…</span>bravo` left a 33pt gap, because the hidden
element's own margin and padding still advanced the cursor.

## Deliberately not done

- **Not a parser-side wrap.** Wrapping the run in an anonymous block would split `a<span hidden/>b` into two
  blocks (`JoinsTheInlineRun(none)` is false) and put a line break between `a` and `b`.
- **The implied `<html>/<head>/<body>` is still not built** (#1370: `body {}` rules do not apply to a
  fragment). Unrelated to this fix and unchanged.
- **`<span hidden>`** is not covered by a test: the `hidden` attribute is not mapped to `display: none` by this
  engine's UA sheet, so that markup is not a `display: none` element here at all
  ([gap](../accepted-gaps/the-hidden-attribute-has-no-effect.md), #1380).

## Evidence

- `DisplayNoneAmongInlineContentTests` (19 cases): 12 fail on the unfixed source, all 19 pass with it. None use
  `LayoutHarness.Wrap`, which adds a `<body>` and so never builds the failing tree.
- Differential run against v0.9.18/v0.9.19 with `<style>`/`<meta>`/`<link>`/`<head>` before float + text, before
  abs-pos + text, before `span[div]` + text, and `a<script/>b`: text renders in every shape; controls unchanged.
- Rules to keep: a float counts toward neither half in `ContainsVariantBoxes` (a lone float otherwise wraps
  into itself forever) and `IsAtomicInlineLevel` is not widened (#473). See
  [../invariants/dom-the-inline-content-predicates-must-agree-on-display-none.md](../invariants/dom-the-inline-content-predicates-must-agree-on-display-none.md).
