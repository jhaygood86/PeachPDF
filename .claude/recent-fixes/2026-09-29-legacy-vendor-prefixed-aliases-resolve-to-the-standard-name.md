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

**Not done (tracked separately):** `display: -webkit-box` + `-webkit-line-clamp`, `-webkit-gradient()`, prefixed
sizing keywords, and prefixed forms of properties with no standard support here.

**Not aliased on purpose:** `-webkit-column-break-*` (legacy `always` value differs from `break-*`), `-o-`/`-ms-`
property prefixes (never widely needed for the properties above).
