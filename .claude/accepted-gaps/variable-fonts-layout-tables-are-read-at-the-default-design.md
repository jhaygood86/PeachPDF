# Variable fonts: FeatureVariations are not applied

`Typeface.WithAxes` moves TrueType outlines (`gvar`), advance widths (`HVAR`, or `gvar` phantom points), font-wide metrics (`MVAR`) and
the deltas of `GPOS` value records and anchors (the `VariationIndex` device tables, resolved against the item variation store in `GDEF`:
`GposValueRecord.Variation`, `GposAnchor.XDelta`/`YDelta`, `OpenTypeDescriptor.Vary`). What it does not do is apply the
`FeatureVariations` of `GSUB` and `GPOS`: the condition sets that swap a feature's lookups at a region of the design space (`rvrn`
glyph swaps at a weight, for instance). Shaping an instance never switches a feature on or off because of a location. Tracked in
[#1406](https://github.com/jhaygood86/PeachPDF/issues/1406).

Doing it means resolving a feature's lookup list at a location: `GsubShaper` and `GposPositioner` reach `GetActiveLookupIndices` from
about ten call sites and cache the answer per table and `ShapeSettings` (`ConditionalWeakTable`), so the location (its normalized
coordinates) has to be threaded through them and into those cache keys, or the answer for one instance is served to another.
