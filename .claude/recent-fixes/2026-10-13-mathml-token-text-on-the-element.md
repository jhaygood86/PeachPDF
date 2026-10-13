# A MathML token's text stays on its element instead of in a child text box

math-l (6,000 display equations, 104,006 tags) built 168,000 boxes: one per tag, plus one text box for the single text node
inside every `mi`/`mo`/`mn` (64,000 of them, 38%). Inside a `<math>` the descendant boxes are only a data source for
`MathTreeBuilder` (`CssBoxMath` never lays them out, and clears them after reading), yet each paid for parsing, a cascade pass
and a `DerivedStyle`.

**The change.** While the HTML parser builds a MathML token element (`mi`, `mo`, `mn`, `mtext`, `ms`, inside foreign content) that
has no children yet, its text goes to `CssBox.ForeignText` instead of a new text box (`HtmlParser.AddTextBox` ->
`TryAbsorbForeignText`). The invariant is "`ForeignText` non-null means the element has no other children": the first child box added
(constructor or `ParentBox` setter) turns the text back into a real leading text box first (`MaterializeForeignText`), and text that
arrives after a child exists is an ordinary text box, so interleaved content such as `<mtext>a<b>x</b>c</mtext>` keeps its order.

**Everything that read those text boxes was found and taught the field**: `CssBoxMathSourceNode.GetTextContent` (hence the
serialized source for tagged PDF), `CssContentEngine.GetTextContent` and `CssNamedStringEngine`'s two element-text readers (a heading
containing math still contributes its tokens to `string-set`/`content()`), and `CssBidiParagraphResolver.Flatten`, where an inline
`<math>` is still part of its paragraph's text for the bidi pass (the range now belongs to the element box, whose levels nothing reads).

**Measured** (warm, in-process, math-l): 1.75-1.9 s -> ~1.0 s; allocation 698 -> 518 MB per render; GC heap ~330 -> ~226 MB; GC
pause ~570 -> ~250 ms. Boxes 168,000 -> 104,000.

**Left.** The other 104,000 boxes are the tags themselves. They could be left uncascaded when no rule can match inside a
`<math>` (the cascade, style objects and `DerivedStyle` per token), but author CSS legitimately targets math elements
(`mi { color: red }`, `mathvariant`), so that needs a way to ask "can any rule reach this subtree" first.

**Evidence.** Release suite (new `MathTokenTextTests` for the absorb/materialize/order cases and the readers); PDF words and drawings
identical to main on 19 benchmark documents (math-s/m/l), 199 fuzz documents and all 220 showcase PDFs; 0-warning rebuild.
