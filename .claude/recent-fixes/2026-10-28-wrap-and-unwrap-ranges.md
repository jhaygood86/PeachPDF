# Wrapping a range in an element, and taking one out

`HtmlDocument.WrapRangeAsync`/`UnwrapElementAsync` (`TextEditor.Wrap`/`Unwrap`) apply and remove inline formatting: cut the text boxes at the ends of the range (`SplitLeaf`),
create the element with a new `HtmlTag` (`CssBox.CreateBox(tag, parent)`), cascade it (`DomParser.RecascadeSubtree`), then `RedoText` for the paragraph.

**Found by running it.** The first version moved the existing text box into the new element and re-cascaded it. When the range began at the start of a text box (the
original box moved, not a fresh one), the wrapped word laid out 2.5pt narrower than the same markup loaded fresh: a box keeps what it derived from its style (its font),
and the re-cascade did not refresh it, so the bold word was measured in the regular font. The test that passed had a range in the middle of a text box, which was a fresh
box. Wrap and unwrap now create new text boxes for the moved text, and unwrap refuses an element that holds other elements (their descendants would be reused boxes).
A split or join still re-cascades and moves existing boxes; that is recorded in the accepted-gap note.

**Also**: the copy a split makes no longer carries the `id` (`WithoutId`), as when a browser splits an element.

**Evidence**: `TextEditTests` (7 new): strong at a word, at the start/middle/end of a text box, a link with `href`, unwrap, wrap and unwrap round trip on a late page
(resumed layout) equal to the original, refusals, split without the id. All compare with a fresh load of the equivalent markup.
