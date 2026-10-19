# A multi-layer `background` shorthand whose layers omit a longhand

`background: linear-gradient(to right, #00f, #0f0), rgba(255, 0, 0, .5)` drew only the red wash: the gradient was gone. So was the `no-repeat` of `background: url(a) no-repeat, url(b)`. Measured through the CSS-OM
(`style.GetPropertyValue(...)` after parsing `#a { background: ... }`):

| Declaration | `background-image` / `background-repeat` before |
|---|---|
| `url(a), red` | image `initial` (the layer is lost; colour fine) |
| `url(a) no-repeat, url(b)` | repeat `initial` |
| `url(a), url(b) no-repeat` | repeat `no-repeat` (one value, so both layers get it) |
| `url(a) no-repeat, url(b) repeat-x` | both right (every layer names the longhand) |

## Cause

`EndListValueConverter`'s `ListValue.ExtractFor(name)` joins the per-layer tokens of one longhand with commas. A layer that leaves the longhand out contributes an empty value, and the join still wrote the comma: `url(a.png),`.
No longhand accepts a trailing comma, so the whole longhand fell back to `initial` (a *leading* omission, `url(a), url(b) no-repeat`, wrote no comma at all and silently shortened the list instead). The final layer being only a colour is
the commonest way to omit the image, which is why `gradient, colour` is what showed it.

## Fix

`RequiresEnd` takes the per-layer initial values of the layered longhands (`BackgroundProperty.LayerDefaults`: `none`, `0% 0%`, `auto`, `repeat`, `scroll`, `padding-box`, `border-box`); a layer that omits one takes it
(CSS Backgrounds 3 section 2.10), and when no layer names the longhand it stays `initial`. A longhand that is not listed (the colour, which only the last layer sets) is taken from the layers that set it and an empty layer is
skipped, instead of leaving a hole. `cursor` uses the same converter without defaults and is unchanged. The defaults are tokenized once, when the converter is built (`ExtractFor` runs for every longhand of every declaration).

An empty layer (`url(a),`, `url(a), , url(b)`, `, url(a)`) is rejected: the layer converters accept zero tokens, and the hole the old join left in the longhands had been hiding that, so once the join stopped producing
a hole `url(a),` would have painted its image where a browser drops the declaration.

A trap found by the third review: the shorthand now exports `background-attachment` as one entry per layer (`scroll, fixed`), and the PeachPDF property registry (`css-properties.json`) had it as a single keyword, so
the list was rejected and `url(a) no-repeat, url(b) fixed` lost its `fixed` (before, the join skipped the omitting layer and exported the lone `fixed`, which was accepted - by accident, for the wrong layer). `background-attachment`
is now a `keyword-list` like its siblings (`background-repeat`, `-origin`, `-clip`); the painter already resolved the attachment per layer (`CssImagePainter`: `LayerAt(SplitLayers(attachmentList), layerIndex)`), and `@supports`
answers from the same entry. Any per-layer longhand the shorthand starts exporting as a list needs a registry entry that takes a list.

## Not done

Serialising the shorthand back (`style.cssText` / `Stringify`) still prints only what the layers hold. `background-position` fills `0% 0%` for an omitted layer, which the CSS-OM prints as `0 0`.

## Evidence

`BackgroundLayerShorthandTests` (21 of its 25 cases fail on the old converter; the other 4 pin that a longhand no layer names, and a colour set by the last layer, are unchanged; every default in `LayerDefaults` is pinned
by a case that fails when it is changed). The render test also compares the page's content streams with the one the equivalent longhands make. The issue's own repro rasterized through PDFium and MuPDF now matches Chrome
(`(0,13,241)`, `(0,128,127)`, `(0,243,12)` across the box; it was `(255,126,126)` flat), and the `gradients` showcase has a new section with four layered cases that match Chrome in both renderers.
