# Variable fonts: the layout tables are read at the default design

`Typeface.WithAxes` moves TrueType outlines (`gvar`), advance widths (`HVAR`, or `gvar` phantom points) and font-wide metrics (`MVAR`).
The variation data of the layout tables is not read: the `VariationIndex` device tables and `ValueRecord` deltas in `GPOS`, the
`ItemVariationStore` in `GDEF`, and the `FeatureVariations` of `GSUB` and `GPOS`. Shaping an instance positions glyphs with the default
design's kerning and mark offsets and never switches a feature on because of a location. Tracked in
[#1406](https://github.com/jhaygood86/PeachPDF/issues/1406); `GposPositioner`'s and `GsubShaper`'s per-table caches are keyed by table
and would have to gain the location in their key.
