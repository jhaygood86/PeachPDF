# The predicates that decide how a run of inline content is flowed must agree on `display: none`

`DomUtils.ContainsInlinesOnly` (what `CssBox.LayoutContents` dispatches on), `DomParser.ContainsVariantBoxes`,
`DomParser.ContainsInlinesOnlyDeep` and `DomParser.JoinsTheInlineRun` are one question asked in four places:
which children does an inline formatting context take, and which does it step over. A `display: none` child
generates no box (CSS Display 3 §2.5), so all of them step over it.

**Symptom when one disagrees:** text silently vanishes. The parser builds no implied `<body>`, so head content
(`<style>`, `<meta>`, `<link>`) is a `display: none` sibling of the bare text after it. If the dispatch predicate
counts that sibling as block-level, the run takes the block-children path and its bare text box is never
measured or placed - no exception, just no text (and blank pages when it was long). It was present from 0.9.18
for `<style>text` and was exposed for `float + text` by #1205.

**Rules:**
- A new predicate in this family must skip `display: none`.
- `FlowBox` must step over such a child itself: its margin, border and padding must not advance the cursor.
- A float counts toward neither half of `ContainsVariantBoxes`.
- A test for text in a fragment must not use `LayoutHarness.Wrap`, which adds the `<body>` that hides the whole
  class.

Origin: `.claude/recent-fixes/2026-09-25-display-none-sibling-no-longer-drops-inline-content.md`.
