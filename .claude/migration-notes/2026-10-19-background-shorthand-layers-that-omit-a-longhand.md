# A multi-layer `background` shorthand keeps the layers' images, repeats and positions

`background: linear-gradient(...), #colour` (a gradient layer, then a last layer that is only a colour), and a shorthand whose layers do not all name the same sub-properties (`url(a) no-repeat, url(b)`), used to lose the
longhand some layer omitted: the gradient or image disappeared, and a `no-repeat` was dropped or applied to every layer. Each layer now takes the initial value of what it leaves out, as in a browser, so the gradient draws over the
colour and `no-repeat, repeat` applies per layer. A document that wrote `background: <image>, <colour>` and saw only the colour now shows the image
Two more things a document author can see: a multi-layer `background-attachment` list (`fixed, scroll`, and the shorthand's `url(a) fixed, url(b)`) now applies per layer, where it was rejected and the layers fell back to
`scroll`; and an empty layer (`url(a),`, `url(a), , url(b)`) now makes the whole declaration invalid, as in a browser, where it used to be accepted (`url(a),` reset every background longhand to its initial value)
(`git show v0.9.23:src/PeachPDF/CSS/ValueConverters/EndListValueConverter.cs` joins an omitted layer as an empty list entry, and `v0.9.23:src/PeachPDF/css-properties.json` has `background-attachment` as a single keyword).
