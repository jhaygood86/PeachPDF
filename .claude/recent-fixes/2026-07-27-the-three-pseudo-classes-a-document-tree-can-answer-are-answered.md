# The three pseudo-classes a document tree can answer are answered

Issue #417. `:empty`, `:any-link` and `:scope` sat in `UnmatchableSelectors.PseudoClasses` alongside
`:hover`/`:checked`/`:host`, so they parsed and selected nothing. The rest of that list belongs there
permanently — a static PDF has no interaction, form state, shadow tree or browsing history — but these
three depend only on the document tree, which is exactly what the matcher already walks.

## The load-bearing idea

`:scope` and `:any-link` really are one-line arms. With no scoping root in play `:scope` is the
document's root element (Selectors 4 §6.6), i.e. literally the `:root` arm; and `:any-link` is the union
of `:link` and `:visited`, so with `:visited` never matching it is literally the `:link` arm.
`DoesSelectorMatch(PseudoClassSelector, …)` now folds each pair into one branch rather than duplicating
the body, which is also the honest statement of *why* they agree.

`:empty` needed a new question the interface could not answer. It is not derivable from
`ICssDomNode.Children`, so it is a new `ICssDomNode.IsEmpty` member each node kind implements over its
own tree, for two independent reasons:

- The HTML box tree keeps text in **anonymous child boxes**, so `Children` alone cannot distinguish
  `<p></p>` from `<p>x</p>` — the text is on a child's `Text`, which the interface does not expose.
- `SvgXmlDomNode.Children` deliberately exposes **elements only** (`XElement.Elements()`), so a
  standalone SVG's text nodes are invisible to any generic walk. It answers off `Nodes()` instead.

## Found by running it, not by reading it

The trap that decides the HTML implementation: **`::before`/`::after` boxes already exist by the time
`:empty` is asked.** PeachPDF's UA stylesheet carries a blanket `:before, :after { white-space: pre-line }`
that matches every element, and pseudo-element boxes are synthesized *as a side effect of a rule
matching* — inside the same `CascadeApplyStyles` pass, with the UA rules gathered before the author
rules. So by the time an author's `p:empty` is evaluated, every `<p>` already has two generated children.
`CssBox.IsEmptyElement` skips `::before`/`::after`/`::marker` for that reason, which is also what the
spec wants (`:empty` is defined over the source tree, and generated content does not affect it).

`::first-letter` is deliberately **not** skipped: unlike the others it holds real source text, split out
of a text box that keeps only the remainder, so skipping it would make `<p>x</p>` look empty to a
document that styles `p::first-letter`.

The second trap cost a test rather than a bug: `CssBoxSvg.EnsureDocument` **clears its child boxes**
once the scene graph is built, so an inline `<svg>`'s `<rect>` boxes do not exist after a layout pass.
The `SvgCssBoxDomNode` test reads the DOM straight out of `SetHtml` instead — which is also the only
window in which that node type is ever asked anything in production.

## Deliberately not done

`:link`/`:any-link` still never match an SVG `<a href>`, even though PeachPDF renders one as a real PDF
link annotation. Both go through `CssBox.IsClickable`, which is `CssBox`-only — and which
`CssBoxFrame` overrides to `true`, so a generic "is this an `<a>` with an `href`" rewrite would have
silently changed which elements `:link` selects. Keeping `:any-link` byte-identical to `:link` was worth
more than widening both.

## Evidence

Full suite green (6594 passed, net8.0). 30 new tests in
`src/PeachPDF.Tests/CSS/DocumentTreePseudoClassTests.cs` covering the white-space/comment/`&nbsp;`/void-
element boundary cases, generated-content and marker independence, combinators and `:not(:empty)`,
`:any-link` ≡ `:link`, `:scope` ≡ `:root` on every box in a document, and `:empty` over both SVG node
kinds. A new `document_tree_selectors` showcase renders all three.
