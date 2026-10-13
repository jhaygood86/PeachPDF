# An inline `<math>` keeps its markup as source elements, not boxes

`math-l` (6,000 equations, ~100,000 MathML elements): warm allocation 446 -> 272 MB, warm render ~1.2 s -> ~0.85 s; `math-m` 0.27 -> 0.22 s.
Output is unchanged: `MathSourceParityTests` renders 56 documents both ways (page content, and tagged) and requires identical bytes;
PyMuPDF words and drawings are identical to main on all 19 benchmark documents, the fuzz corpus and the 220 showcases.

## The idea

`CssBoxMath` never lays out or paints its descendants: `MathTreeBuilder` reads each element's name, attributes, text and cascaded `color` once, and
the boxes are then cleared. Font and font size come from the `<math>` box itself (and `mathsize`), not from the descendants. So a box per element,
with its own `ComputedStyle`/`DerivedStyle`, was paid for a name, some attributes and a colour.

`HtmlParser.MathSourceParser` now builds `MathSourceElement`s (`MathML/MathSourceElement.cs`; element or run of text, implementing both
`IMathSourceNode` and `ICssDomNode`) while it stays within what that can take. `CssBoxMath.SettleSource` runs at the cascade, where the document's
stylesheets are finally known (`CascadeParseStyles` runs after the parse), and either keeps the tree or calls `MaterializeSource`, which builds
exactly the boxes the parser would have (text kept on token elements via `ForeignText`, as before).

## When the tree stays

`CssData.AnyConsequentialRuleMatches` says no rule that declares anything but purely visual properties matches any element, and no element has a
`style`, `color` or `id` attribute. A `color` attribute is a presentational hint for `color` (`DomParser.TranslateAttributes`); an `id` is an anchor
target. The allowlist (`DeclaresAConsequentialProperty`) is font/text/margin/padding/border/background/outline/min/max, size, spacing,
`box-sizing`/`box-shadow`/`vertical-align`/`white-space`/`opacity`/`cursor` and custom properties. It is an allowlist, not a list of the properties
known to matter, because the boxes are read by code that nobody enumerated: counters, `string-set`, bookmarks, `content`, floats and so on all
take effect through a box that is never laid out. A new property is consequential until someone argues otherwise.

A rule for `::before`/`::after`/`::marker` counts without its other conditions being tested (it adds a box to the element; matching a
pseudo-element compound against a `CssBox` *creates* that box, so the light nodes use `SubjectWithoutPseudoElementMatches`, which is pure). The UA
sheet's `br::before`, `q::before` and `*::footnote-*` rules are indexed as universal rules: a first version that treated any `::before` rule as
matching sent every document to boxes. The other pseudo-elements add nothing to an element's own box.

## What hands back to the ordinary path

The parser answers `Abandon` before changing anything for: an element name that is not in `MathElementNames` (so `<b>` in an `mtext`, `<svg>`,
`<foo>`, upper-case `<MI>`), an end tag for nothing it has open, `</p>`, and a nested `<math>` (the outer builder reads boxes under it). The open
source elements become boxes, `curBox` is the box for the open one, and the token is dispatched as it always was. This keeps the odd inputs exact
without re-implementing `FindParent`/`CanEndTagBeOmitted` for them.

## Readers of a descendant's text

Before layout, `CssContentEngine.GetTextContent` and `CssNamedStringEngine.CollectTextFromChildren` read the host's text through the child boxes; for
a math host they now append the source runs where the child boxes' text would be (after a `::before` box, before the rest). `:has()`, `:empty`
and sibling selectors read `CssBox.SelectorChildren`/`IsEmptyForSelectors`, which `CssBoxMath` answers from the source tree. Bidi, `dir=auto`,
`::first-letter`, hyphenation and tagging were checked by the parity corpus and needed nothing.

## Found on the way

- `:empty` on a token (`mi:empty`) matched whenever the token had text, since #1734 moved the text off the box tree; `IsEmptyElement` reads `ForeignText` now.
- `dir="auto"` stopped seeing a token's text when #1734 moved it onto the element: `ScanForStrongDirection` read only `Text`. A paragraph
  whose first strong character was a Hebrew `mi` resolved ltr. It reads `ForeignText` and a math's source runs now.
- A math whose markup is materialized at the cascade has its own `::after` box already, generated before `SettleSource` ran; the children are put
  in front of it, as the parser had them. `::first-letter` rules count like generated content (they split an element's text).
- The corpus is the test: the readers above were found by looking, but the first version of the allowlist predicate was only caught by running real
  documents (the UA sheet) through it. Add a case to `MathSourceParityTests.Documents` before changing any of this.
- Selector enumeration allocates: `compound.LastOrDefault()` and `foreach` over a `Selectors` box an enumerator. The first version of the predicate
  did that per element and was 17% of the allocation it was meant to save. The analysis of each rule is done once when the index is built.
- After layout the tree is dropped, like the boxes were (`Boxes.Clear()`); a test that wants to look at it reads it between parse and layout.

## Not done

Attributes still go through `HtmlTag`'s dictionary and the tokenizer's own attribute objects (~7% of `math-l`'s allocation). `MathSourceElement` could
hold the two or three attributes the builder reads and serialize the rest lazily, but the serializer wants all of them.
