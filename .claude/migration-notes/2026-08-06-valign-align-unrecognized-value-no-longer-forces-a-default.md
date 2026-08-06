# An unrecognized `valign`/`align` attribute value no longer overwrites a cascaded `vertical-align`/`text-align`

**Landed:** 2026-08-06 — Fix open alignment issues (#642)
**Doc section:** docs/html-css-support.md (presentational attribute handling is not separately documented
as a table; this is an internal-correctness fix to existing behavior)

`DomParser.TranslateAttributes`'s handling of the legacy `valign` attribute, and the generic `align`
attribute's non-text-align fallback, unconditionally assigned `box.VerticalAlign` even when the attribute's
value didn't match any recognized keyword — forcing `vertical-align: baseline` and silently overwriting
whatever the CSS cascade (author stylesheet or the UA default stylesheet) had already produced. A `<td
valign="bogus">` with no other styling used to render at `baseline` instead of the UA default `middle`;
a `<td valign="bogus">` whose author stylesheet declared `vertical-align: top` used to render at
`baseline` instead of `top`.

An unrecognized `valign`/`align` value is now a no-op: it leaves whatever `vertical-align` the cascade
already resolved in place, matching how browsers generally treat an invalid enumerated presentational
attribute value (as if the attribute were absent).

A related case-sensitivity bug is fixed in the same change: `align="LEFT"`/`align="CENTER"` etc.
(case-insensitive matching is the norm for legacy HTML attribute values) previously missed the
case-sensitive comparison against `text-align`'s four horizontal keywords and fell into the
`vertical-align` branch instead, clobbering it the same way. `align="LEFT"` now correctly maps to
`text-align: left` regardless of case.
