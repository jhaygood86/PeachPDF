# A `transform` on an `<embed>` is ignored

CSS Transforms 1 §2 makes an atomic inline-level box transformable, and a browser treats `<embed>` as a replaced
element, so it applies the `transform` (headless Chrome: `<embed width="40" height="20" src="data:image/png...">` is a
40 x 20 box and rotates). PeachPDF has no replaced box for `<embed>`: it is a plain `display: inline` `CssBox`, which
`DerivedStyle.IsTransformable` (it asks `MonolithicContent.IsReplaced`, which does not know `embed`) therefore treats as a
non-transformable inline box and ignores the `transform`.

Left alone because nothing is drawn for an `<embed>` to transform: it renders no image or SVG, ignores `width`/`height`,
and `embed` is missing from the void-element list (`HtmlUtils.IsSingleTag`), so the content after it becomes its child.
The deviation disappears once `<embed>` is a replaced element, which is the real defect; adding a special case for it
in `IsTransformable` first would have made a transform apply to an element that draws nothing.

Filed as [issue #1707](https://github.com/jhaygood86/PeachPDF/issues/1707).
