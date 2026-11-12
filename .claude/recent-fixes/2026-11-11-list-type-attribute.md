# `type` on ol/ul/li as a list-style-type hint

Added `DomParser.TranslateListType`, called from `TranslateAttributes` (the hint step between the UA sheet and author rules), so author CSS and
inline style beat it with no extra code. `ol`/`li` values (`1 a A i I`) are case-sensitive, `ul`/`li` values (`disc circle square`) ASCII
case-insensitive; an unrecognized value leaves the box alone. `ListStyleType` is inherited, so the `ol`'s hint reaches its items and `li type`
overrides one item. Tests: `ListTypeAttributeTests` (box-tree marker text/shape).
