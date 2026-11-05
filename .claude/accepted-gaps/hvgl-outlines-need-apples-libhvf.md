# hvgl glyphs are drawn only where Apple's libhvf exists

Fonts whose outlines are an `hvgl` table (PingFang on current macOS) have no `glyf` or CFF outlines, so they can be drawn
only through Apple's `libhvf` (`/usr/lib/libhvf.dylib`: macOS and Mac Catalyst 15.4+, iOS and tvOS 18.4+). Below those
versions, off the Apple platforms, or if the library cannot be loaded, such a face has no outlines (`Typeface.TryGetOutline`
is false and the text is not drawn), exactly as before. Nothing else needs the library: ordinary fonts, including Apple's own
TrueType and CFF ones, are unaffected on every OS version.

A managed reader of the table was considered and rejected: Apple documents the table layout but not the blend formula,
the units or the meaning of the blend types, and neither FreeType (which calls the same library) nor HarfBuzz has an
implementation to port. The decision to call the library, as FreeType does, is the user's.
