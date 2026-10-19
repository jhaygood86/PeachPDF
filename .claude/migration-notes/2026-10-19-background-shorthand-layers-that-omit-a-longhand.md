# A multi-layer `background` shorthand keeps the layers' images, repeats and positions

`background: linear-gradient(...), #colour` (a gradient layer, then a last layer that is only a colour), and a shorthand whose layers do not all name the same sub-properties (`url(a) no-repeat, url(b)`), used to lose the
longhand some layer omitted: the gradient or image disappeared, and a `no-repeat` was dropped or applied to every layer. Each layer now takes the initial value of what it leaves out, as in a browser, so the gradient draws over the
colour and `no-repeat, repeat` applies per layer. A document that wrote `background: <image>, <colour>` and saw only the colour now shows the image
(`git show v0.9.23:src/PeachPDF/CSS/ValueConverters/EndListValueConverter.cs` joins an omitted layer as an empty list entry).
