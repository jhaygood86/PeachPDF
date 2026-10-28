# Highlight and structural-edit gaps closed (issues 1968-1972)

**Highlights** (`FragmentPainter.Selection.cs`): `HighlightPaint` carries each layer's resolved style. The fill is the whole line box
(`FillBand`: `word.Line.LineTop/LineBottom` mapped into the fragment's space by `rect.Y + (LineTop - word.Top)`); `text-shadow` and `-webkit-text-stroke*`
that differ from the element's own change the stretch's draw (`DrawWordGlyphs(effectsStyle)` swaps the style source for the shadow and stroke painters only);
a `text-decoration` the highlight adds is drawn with `PaintDecoration(..., firstLineStyle: style, ownDecorationArea: false)` over the covered span, so
decoration geometry (offset, thickness, style, colour) is the existing code's, not a copy. "Differs from the element's own" is the declared test, as it was for
`color`. `DomParser.ResolveHighlightStyle` now lets a child highlight with rules of its own inherit `color` and `text-shadow` an ancestor highlight declared.

**`::target-text`**: `HtmlDocument.ApplyTextFragmentDirective` finds a `text=` directive with `FindText` plus prefix/suffix context checks and sets the
`target-text` highlight.

**Structural edits**: `DomParser.RecascadeSubtree` (`CascadeApplyStyles` on one box, as markers and placeholders already do) runs for the kept paragraph, the new one
and the next after a split, and for the survivor and next after a join. Found by test: copying the style (`AdoptComputedStyle`) gave both halves the
`p:last-child` rule.

**Found by running it**: vertical text has *no* text runs (`TextRunCollector` skips it), so highlighting it is not a painter problem: a painter generalised to the block
axis was written, found unreachable, and removed. Transforms already worked (the fill is drawn in the element's transformed space); the test pins it.

**Not done**: vertical text (#1970), `fill`/`stroke`/`caret-color`, `:nth-child` of later siblings and duplicate ids (#1972); see the accepted-gap notes.
