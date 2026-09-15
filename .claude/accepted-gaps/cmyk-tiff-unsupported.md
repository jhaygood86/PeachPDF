# CMYK TIFF images are rejected instead of embedded

Tracking issue: [#1096](https://github.com/jhaygood86/PeachPDF/issues/1096).

Issue #1085 (preserve CMYK/YCCK image data end-to-end) made PeachPDF stop forcing a CMYK JPEG through
a naive RGBA32 conversion (which destroys print separations with no color management at all) and
embed it instead via byte-for-byte pass-through, using its embedded ICC profile when present and
requiring one under PDF/A conformance.

TIFF is the only other PeachImage codec that decodes to `PixelFormat.Cmyk32` (`PeachImageSource.cs`
routes on `Image.Identify(stream).PixelFormat == PixelFormat.Cmyk32` regardless of source format), but
PeachImage's TIFF decoder doesn't currently surface an embedded ICC profile the way its JPEG APP2
handling does (no support for the ICC profile tag, 34675/0x8773) - `Image.Metadata.GetIccColorProfile()`
never returns non-null for a TIFF source. Rather than embed a CMYK TIFF anyway via a lesser, ICC-less
path - repeating the exact kind of silently-wrong CMYK handling #1085 exists to fix, just for a
different format - `PeachImageSource.Decode` rejects it outright: `InvalidOperationException`, the same
non-fatal "this image doesn't render" contract every other unsupported format (TGA/PSD/HDR - see
[tga-psd-hdr-unsupported.md](tga-psd-hdr-unsupported.md)) already has.

This is a deliberate, temporary *narrowing* of behavior, not a new limitation to apologize for: a CMYK
TIFF used to render (via the pre-#1085 naive forced-RGBA32 conversion), just with the same kind of wrong
colors #1085 describes for JPEG. A loud non-render is strictly more correct than a silent wrong-color
one, and matches how #1085 treats every other case where full color fidelity can't be guaranteed.

Closing this needs PeachImage's TIFF decoder to surface an embedded ICC profile (tag 34675) the way its
JPEG APP2 handling already does, and then PeachPDF re-enabling CMYK TIFF support behind the same shape
JPEG uses: decode natively to `Cmyk32` (never RGBA32), embed via a raw `/FlateDecode` CMYK stream (no
PDF-native pass-through filter exists for TIFF the way `/DCTDecode` does for JPEG, so it can't be
byte-for-byte the way a CMYK JPEG is), `/ICCBased` when a profile is present, and the same
PDF/A-requires-an-ICC-profile gate (`PdfACmykImageGuard`) JPEG already has.
