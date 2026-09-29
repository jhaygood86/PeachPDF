# Legacy vendor-prefixed aliases resolve to the standard name before a property is created

**Load-bearing idea:** an alias is canonicalized in `PropertyFactory.Create*`/`IsShorthand`/`GetLonghands`
(via `VendorPropertyAliases`) *before* the property object exists, so the property, the cascade, `var()`, `inherit`/
`initial`/`revert`, `@supports` and CSSOM reads only ever see the standard name. This replaces the
`-webkit-backdrop-filter` model (a second full `css-properties.json` entry whose `Property.Name` stayed prefixed).

**Trap that model had:** `DomParser.AssignCssBlock` keys `pendingVarProperties` by `prop.Name`, so
`box-sizing: var(--s); -webkit-box-sizing: border-box` left the pending `box-sizing` un-cleared and it won
afterwards. Canonical names remove that whole class of ordering bug; `VendorPrefixedAliasTests` pins it.

**Values are not names:** the box layer compares `display`/`position` by keyword *text* (`Keywords.Flex`), so
`Map.DisplayModes`/`PositionModes` accepting `-webkit-flex` is not enough — `VendorValueAliases.Normalize` rewrites
it in `DomParser.AssignCssBlock` before the box sees it.

**Gradients:** rewritten at token level in `ValueBuilder.Apply` (the one place every declaration value passes), not in
the two gradient parsers. Legacy angle is `90° − θ` (counter-clockwise from east), legacy `top`/`left` name the start
edge (flip to `to bottom`/`to right`), radial puts the position first and uses `cover`/`contain` (mapped to
`farthest-corner`/`closest-side`). Serialized corner order is horizontal-first (`to left top`).

**Evidence:** `VendorPrefixedPaintTests` rasterizes each prefixed form and compares every pixel to its standard
spelling (plus direction sanity checks so a mutual blank render can't pass); the `vendor_prefixed_css` showcase was
rasterized with PDFium and MuPDF and agrees.

**2009 flexbox / `-webkit-line-clamp`:** `-webkit-line-clamp` is a plain alias of `line-clamp`; `-webkit-box-flex` and
`-webkit-box-ordinal-group` alias `flex-grow`/`order`. The container properties (`-webkit-box-orient`/`-direction`/
`-pack`/`-align`) need *value* translation, so they are real OM properties (`LegacyBoxProperty`) with no computed
storage: `LegacyBox.TryApply` (called from `DomParser.AssignCssBlock`) rewrites each onto the standard flex property,
composing `orient`+`direction` into `flex-direction` from the box's current value so their order doesn't matter.
`display: -webkit-box` cannot be resolved per-declaration (orientation may come later), so `Map.DisplayModes` accepts it
as `Flex` and `LegacyBox.Resolve` (cascade step before `BlockifyPositionedBox`) turns it into `block` when the final
`flex-direction` is a column, else `flex`. Vertical → **block, not flex column** is deliberate: a text child of a flex
container becomes an anonymous flex *item*, and `line-clamp` (non-inherited) on the container would never reach it.

**Not done:** `-webkit-gradient()`, prefixed sizing keywords, and prefixed forms of properties with no standard support here.

**Not aliased on purpose:** `-webkit-column-break-*` (legacy `always` value differs from `break-*`), `-o-`/`-ms-`
property prefixes (never widely needed for the properties above).
