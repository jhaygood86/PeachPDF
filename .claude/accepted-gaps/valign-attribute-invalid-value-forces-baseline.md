# Unrecognized `valign`/`align` attribute value forces `vertical-align:baseline`

Tracking issue: [#642](https://github.com/jhaygood86/PeachPDF/issues/642).

`DomParser.TranslateAttributes`'s handling of the legacy `valign` attribute and the generic `align`
attribute's non-text-align fallback (`Html/Core/Parse/DomParser.cs`) unconditionally assigns
`box.VerticalAlign`, even when the attribute's value doesn't match any recognized keyword. An
unrecognized value (e.g. `<td valign="bogus">`) forces the cell to `vertical-align: baseline`,
overwriting whatever the CSS cascade had already produced - for a `<td>` with no explicit
`vertical-align` declared, that's the UA default stylesheet's `middle` (`tfoot, tr { vertical-align:
middle }` + `td, th { vertical-align: inherit }`, `CssDefaults.cs`).

Confirmed: a `<td>` with no `valign` attribute resolves to `VerticalAlignment.Middle`; an otherwise
identical sibling `<td valign="bogus">` resolves to `VerticalAlignment.Baseline` instead of also being
`Middle`. Per how browsers generally handle HTML presentational-attribute hints, an unrecognized/invalid
enumerated attribute value should be treated as if the attribute were absent, leaving normal CSS
cascade/inheritance in place.

This predates the `vertical-align` typed-storage conversion that surfaced it while doing a compiler-driven
review of every `VerticalAlign`-assigning call site - the old raw-string code produced the same net effect,
since an unrecognized string also failed to match any keyword in the downstream layout switches.

**Deliberately out of scope.** Fixing this means `valign`/`align`'s unrecognized-value case must skip the
assignment entirely (leaving `box.VerticalAlign` at whatever the cascade already resolved) instead of
calling through `CssProperty<VerticalAlignment>.FromCssText`'s keyword-or-fallback shape, which always
assigns something - a real behavior change to presentational-attribute handling, not a storage-type change.
