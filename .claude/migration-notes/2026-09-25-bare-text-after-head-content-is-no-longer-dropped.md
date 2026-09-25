# Bare text after `<style>`/`<meta>`/`<link>` in a fragment with no `<body>` tag is no longer dropped

**Landed:** 2026-09-25
**Doc section:** none - the docs already describe this as working; the parser's lack of an implied `<body>` is unchanged
**Verified against v0.9.19:** built the CLI at the tag. `<style>p{margin:0}</style><div style="float:left;width:80pt">F</div>alpha bravo`
rendered only `F` (0.9.18 rendered the text; the regression came with #1205 in 0.9.19), and `<style>…</style>alpha bravo`
with no float rendered no text at all in both 0.9.18 and 0.9.19.

Before: a document that starts with head content and has no `<body>` tag - typical of templated body
fragments (`<style>…</style>` followed by content) - lost any run of bare text that had no block element around
it. After a float it disappeared, and long text left blank pages; on its own it disappeared. Now the text
renders, beside the float where there is one. Wrapping the text in a `<p>` or `<div>`, or writing the `<body>`
tag, was and is unaffected. The same applies to text next to any other `display: none` element inside a
block: `a<script>…</script>b` and `a<span style="display:none">…</span>b` now lay out as one line, with none of the hidden
element's margin or padding between the two runs.
