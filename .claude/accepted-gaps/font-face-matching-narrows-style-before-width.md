# Font face matching narrows by style before width

`FontResolver.TryFindNearestFace` narrows a family's candidate faces by slant first, then width, then weight. CSS Fonts (section 5.2
of Levels 3 and 4) narrows by width first, then style, then weight. The two only differ for a family whose faces differ in both width and
style: a `condensed italic` request against a condensed upright face and a normal-width italic face gets the italic one here and the
condensed one in a browser. Tracked in [#1444](https://github.com/jhaygood86/PeachPDF/issues/1444).

A related gap: among several faces that are oblique over a range, the request's own angle does not choose the nearest range (with
`oblique 0deg 10deg` and `oblique 20deg 30deg` declared, `oblique 25deg` takes the last declared one, and a request for 15deg does not
prefer the nearer range); the angle only sets the `slnt` axis of the face that was chosen. CSS Fonts 4 section 5.2 picks the nearest
oblique angle first.

It was left alone when `@font-face` ranges landed (ranges only change what a face covers, not the order the axes are narrowed in), so the
existing tests and documented behaviour kept their meaning. Fixing it is a reorder in `TryFindNearestFace` plus the descriptions on
`TypefaceQuery`/`TypefaceFamily` and in `docs/peachdrawing-text.md`.
