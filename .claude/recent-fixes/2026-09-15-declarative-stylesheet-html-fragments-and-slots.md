# Declarative API: Class/Id/Tag/PageName + document stylesheet, ReadOnlyMemory&lt;char&gt; CSS parsing, HTML fragment splicing, and slots

Four additive features to the declarative document-building API (`PdfGenerator.CreateDocument`): `IContainer.Class`/
`Id`/`Tag`/`PageName` plus `IDocumentBuilder.Stylesheet(PeachPdfCssContent)` for real selector-based styling of a
declaratively-built tree; `ReadOnlyMemory<char>` overloads across the CSS parsing stack; `IContainer.Html(...)` to
splice a parsed HTML fragment into a declarative container; and slot callbacks (`SlotContext`) for replaceable
`<slot>` points inside a spliced fragment.

## Load-bearing idea: builder calls are "inline style", a document stylesheet is "author CSS"

A `ContainerBuilder` decorator (`.Padding(20)`) already writes a final value through `CssUtils.SetPropertyValue` -
the exact dispatcher the real HTML cascade uses - so there's no typed/string bridge needed. The only real problem
is ordering: builder calls execute first (while the tree is built), but selector matching can only run once the
whole tree exists. Modeled as CSS itself already models it: a builder call is inline `style=""` (wins over a
plain author rule, loses to `!important`). Implemented via one small hook (`CssPropertyFactory.Set` records the
property name into a new `CssBox.BuilderSetProperties` set) and one new narrow cascade pass
(`DomParser.ApplyDeclarativeStylesheet`, threading a new `skipPropertyNames` parameter through
`AssignCssBlocks`/`ApplyAuthorRulesInLayerBands`/`AssignCssBlock` - skipped only for the non-`!important` phase).
No UA-rule matching, no defaulting/reset, no inline-`style=""` phase - a declarative box already has final,
directly-assigned style; this only layers a caller's selector rules on top of it.

## A finding that shrank Feature 2's scope: `TextSource` was already `ReadOnlyMemory<char>`-based

`src/PeachPDF/CSS/Model/TextSource.cs` already wraps `ReadOnlyMemory<char>` internally (not just `string`) -
built that way specifically so a `Stylesheet`'s lazy `.Text` read can retain a reference past the parse call. The
user's original ask was a `ReadOnlySpan<char>` overload; asking "is that genuinely zero-copy?" surfaced that a
`ReadOnlySpan<char>` (ref struct) cannot cross `CssParser.ParseStyleSheet`'s own `await` (real for `@import`) or be
retained by the parsed `Stylesheet` - so a Span overload would always need a hidden `.ToString()`, making a
"zero-copy" claim false advertising. Dropped in favor of `ReadOnlyMemory<char>` alone, which needs no such copy
and threads straight through to the existing `TextSource(ReadOnlyMemory<char>)` constructor. No tokenizer/lexer
rework was needed at all - the infrastructure was already there, just not exposed on the public surface.
`string.AsMemory()` is a zero-cost wrap, so every existing `string`-based call site was widened (not duplicated)
by changing internal parameter types from `string` to `ReadOnlyMemory<char>` (implicit widening) rather than
adding parallel private methods.

## The `@page` merge: two independent page-rule tracks that don't know about each other

`PageDescriptorBuilder.PageRules` (declarative `Header`/`Footer` calls) and a document stylesheet's own parsed
`@page` rules both feed `HtmlContainerInt.PageRules`, but naively concatenating them is wrong:
`PageRuleResolver.GetOrderedApplicableRules` keeps only the *last* base-selector rule it sees for margin/size
resolution - so if the stylesheet's base rule sorted after the declarative one, the header/footer's own margin-box
content (`@top-center` etc.) would be silently dropped entirely, not merged. `DomParser.BuildDeclarativePageRules`
merges the two base rules into one instead (margin boxes merged per name, declarative wins a name both declare;
page-level `Style` copied from the stylesheet's base rule except whichever margin/size property the caller marks
"explicit" - simply never copying it, not copying-then-clearing, since `PageGeometryTable`'s later *per-page*
re-resolution of `PageRules` would otherwise silently re-override an explicit `IPageDescriptor.Margin*`/`Size`
call on every page). Track A (`DomParser.CascadeApplyPageStyles`, whole-document base geometry - `MarginTop`/
`CssPageSize` etc.) is a *separate* mechanism from Track B (this per-page-band `PageRules` merge) and needed its
own five `allow*` gates for the identical "don't let the stylesheet override what the builder set explicitly"
reason.

## A design conflict resolved mid-implementation: `IsFragmentStyled` must be per-box, not subtree-skip

Two design passes ran in parallel for the stylesheet-cascade feature and the HTML-fragment-splicing feature. The
first assumed a fragment's root could be flagged and the whole-tree stylesheet pass could just early-return on a
flagged subtree (mirroring the existing `if (box is CssBoxSvg) return;` foreign-content guard elsewhere in
`DomParser`). That's wrong here: a `<slot>` inside a fragment can be filled with *ordinary* declarative content
(built via nested `ContainerBuilder` calls, tracked in `BuilderSetProperties` like anywhere else), and that
content must still participate normally in the whole-tree pass. The actual implementation checks the flag
per-box (skip *applying* declarations to a flagged box - it already went through a real, specificity-ordered
cascade) but *always* recurses into `box.Boxes` regardless - reaching slot-replacement content correctly with no
special re-entry logic, since replacement boxes are simply never flagged in the first place (`MarkFragmentStyled`
runs once, right after the fragment's own cascade finishes and strictly *before* slot processing creates any
replacement boxes).

## A bug the post-change review agent found: a nested `<slot>` still fires its callback after its ancestor is filled

`ContainerBuilder.ProcessSlots` collects every `<slot>` box in one up-front pass (`CollectSlotBoxes`), before any
slot is filled - a `<slot>` nested inside an earlier sibling slot's own fallback content is still in that
collected list even after the ancestor slot's fill detaches its whole fallback subtree (including the nested
slot) from the live tree. The original loop had no check for this: it would still invoke the inner slot's
`onSlot` callback and construct a replacement box, even though that replacement can never reach the final
document (its `ParentBox` chain terminates inside an orphaned subtree, not at the fragment root) - a caller-visible
side effect (the callback runs) for content that's already dead. Fixed with `IsAttached(box, root)`, a plain
`ParentBox`-chain walk checked before invoking each collected slot's callback; skips the callback entirely (not
just its output) once an ancestor slot has already detached the subtree it lives in. Caught by a dedicated review
pass looking specifically for cases where the "collect-then-process" shape could see stale state - not by a
failing test, since no test exercised nested slots before this.

## Evidence

- `PeachPDF.Tests` full suite (net8.0): 11852 passed, 0 failed, 9 skipped (platform-specific, unrelated).
- Diff coverage (`diff-cover` against `origin/main`, all new files staged first): 100% (328/328 lines).
- `dotnet build PeachPDF.slnx -t:Rebuild`: 0 warnings, 0 errors, across net8.0/net10.0/net11.0.
- New TestHarness showcase (`declarative_stylesheet_and_html`) rendered and inspected directly: a document-level
  stylesheet's compound-class selector (`.line-item.highlight`) highlights one table row, a base `@page` rule's
  margin coexists with a `Header`, and an `Html(...)`-spliced paragraph's `<slot>` is filled from C# with bold
  text - confirmed visually correct, not just "didn't throw."
- A dedicated post-change review pass traced all six of the design's own stated invariants by hand (the
  skip-vs-recurse shape, mark-before-process ordering, the three-way `@page` merge, unconditional
  `BuilderSetProperties` population, the zero-copy claim, and `HtmlTag.Name`'s new setter's call sites) and found
  one real defect (the nested-slot issue above), since fixed and covered by a regression test.
