# Text hinting: CFF stem darkening is implemented but not reachable from the public API

The ported CFF engine has FreeType's stem darkening and is tested against FreeType with it on (`CffSize` takes it as an internal constructor argument),
but nothing public turns it on, as FreeType's driver default leaves it off. Exposing it needs a public knob (a `GridFitting` value or a field of
`OutlineRequest`, maybe a `PdfGenerateConfig` option) with the default off. Tracked in [#1442](https://github.com/jhaygood86/PeachPDF/issues/1442).
