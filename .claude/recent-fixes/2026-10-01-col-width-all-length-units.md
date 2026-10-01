# `<col>` width honours every length unit (#1540)

`TryGetColumnElementWidth` only accepted `%`, `px` and unitless, silently dropping `mm`/`cm`/`pt`/`in`/`em`, so the column fell back to the even split (both auto and fixed layout share this helper). It now sends every non-percentage value through `CssValueParser.ParseLength` against the `<col>` box, the same path a `<td>` width uses. CSS 2.2 §17.5.2.1: any non-`auto` column `width` sets the column width.

Test: `ColElementWidth_AnyLengthUnit_SizesTheColumnLikeACellWould` compares the second cell's X with a `<td style=width>` equivalent.
