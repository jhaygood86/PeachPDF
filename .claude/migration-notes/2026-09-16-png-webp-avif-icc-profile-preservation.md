# PNG/WebP/AVIF sources with an embedded ICC profile now preserve it

Before: a PNG's own embedded `iCCP` color profile was silently dropped by its byte-for-byte
pass-through path — the pass-through embed always wrote a bare `DeviceGray`/`DeviceRGB`/`Indexed`
color space, never `ICCBased`, even when the source PNG carried a profile. WebP and AVIF had no way
to expose an embedded profile at all — they always embedded as a raw bitmap under a bare
`DeviceRGB`.

Now (PeachImage 0.4.6): a PNG pass-through embed's color space becomes `ICCBased` (`/N`/`/Alternate`
matching the underlying Gray/RGB/Indexed shape, stream = the profile's raw bytes) whenever
PeachImage can parse the source's `iCCP` chunk into a usable profile of the expected shape —
otherwise the color space stays bare, exactly as before. This needed no new decode step: the
`iCCP` bytes were already available from `PngPassthrough.TryRead`, just previously unused. An
interlaced or 16-bit-per-channel-alpha PNG (neither pass-through- nor alpha-split-eligible, so it
still falls back to a full decode) gets the same treatment from the same already-inflated `iCCP`
bytes, so it isn't a special case either.

WebP and AVIF sources are handled differently, since they have no pass-through mechanism at all —
pixel data still always goes through a full decode and re-embeds as a raw `FlateDecode` bitmap. To
expose the color space, PeachPDF now decodes a WebP/AVIF source a *second* time, natively (no
target pixel format), purely to read `Metadata.Profiles` before the real decode runs — needed
because PeachImage's own pixel-format converter builds a brand-new `Image` with empty metadata
whenever it reshapes pixels (e.g. `Rgb24` → `Rgba32`), dropping the profile that was attached to the
natively-decoded intermediate. A WebP/AVIF source with no embedded profile is unaffected by the
extra decode's result — it still embeds under a bare `DeviceRGB`.

In every case above, the profile also survives a source falling back to a re-encoded JPEG embed
(a lossy-encoded WebP/AVIF, any source being downscaled under `ImageCompression.Auto`, or any
source under `ImageCompression.Lossy`) — re-encoding only recompresses already-decoded samples, it
never changes what color space they're in, so the profile that was already parsed for the
pass-through/raw-bitmap case carries over onto that JPEG embed's `ColorSpace` too instead of being
silently dropped.

A malformed or shape-mismatched profile (wrong color space, wrong channel count) is treated the same
as no profile at all in every case above — the embed falls back to the bare Device* color space
rather than embedding a profile a conformant reader couldn't use.

Tracked as [issue #1106](https://github.com/jhaygood86/PeachPDF/issues/1106).
