# Font face matching narrows by style before width

`FontResolver.TryFindNearestFace` narrows a family's candidate faces by slant first, then width, then weight. CSS Fonts (section 5.2
of Levels 3 and 4) narrows by width first, then style, then weight. The two only differ for a family whose faces differ in both width and
style: a `condensed italic` request against a condensed upright face and a normal-width italic face gets the italic one here and the
condensed one in a browser. Tracked in [#1444](https://github.com/jhaygood86/PeachPDF/issues/1444).

It was left alone when `@font-face` ranges landed (ranges only change what a face covers, not the order the axes are narrowed in), so the
existing tests and documented behaviour kept their meaning. Fixing it is a reorder in `TryFindNearestFace` plus the descriptions on
`TypefaceQuery`/`TypefaceFamily` and in `docs/peachdrawing-text.md`.
