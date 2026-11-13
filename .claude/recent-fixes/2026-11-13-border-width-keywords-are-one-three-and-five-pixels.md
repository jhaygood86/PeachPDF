# `thin`, `medium` and `thick` border widths are 1px, 3px and 5px

`CssValueParser.GetActualBorderWidth` hard-coded the keywords as 1, 2 and 4 layout units. That is wrong twice: browsers (and css-backgrounds-3 §4.2's UA values, which `Length.Thin/Medium/Thick` already encode at the CSS-OM layer) use 1px / 3px / 5px, and the number was a layout unit rather than a pixel, so it did not follow `LayoutUnitsPerPoint`. `border: solid black` with no width is the initial `medium`, which is every test and page that writes `border: solid` and means a browser's 3px line.

Found by the WPT runner: `CSS2/positioning/absolute-replaced-width-001` (a `border: solid black` box drawn 2px where the reference has 3px), and the same one-pixel difference in dozens of css2 tests. The keywords now resolve through `Length.PointsPerPx` and the render context's layout unit, like any other `px` width. They are not snapped further: they are whole pixels already.

## Evidence
+79 passes, 0 lost, across css2-flow, css2-floats, css2-positioning and others; `TheBorderWidthKeywords_AreOneThreeAndFivePixels`; the one existing test that pinned `medium` at 2 units updated; all suites green.
