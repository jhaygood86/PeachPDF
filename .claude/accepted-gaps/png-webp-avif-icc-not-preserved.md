# PNG/WebP/AVIF embedded ICC profiles are not preserved on embed

Issue #1085 added byte-for-byte JPEG pass-through specifically so a JPEG's embedded ICC profile could
survive into the PDF via an `/ICCBased` color space (`PdfImage.EmbedJpegPassthrough`/
`BuildDeviceOrIccColorSpace`). Issue #1086 extended byte-for-byte pass-through to PNG for the pixel data
itself, but deliberately did not extend ICC preservation alongside it: an eligible PNG's own `iCCP`
chunk is never read, and the image is embedded as bare `/DeviceGray`/`/DeviceRGB`/`/Indexed` regardless
of whether the source declared a profile.

The reason is architectural, not an oversight: PNG pass-through's entire point is to skip the full pixel
decode for the common opaque case (`PeachImageSource.DecodePng` calls `PngPassthrough.TryRead`, never
`Image.Load`, whenever a source is eligible). Reading `iCCP` would mean either decoding anyway (negating
that) or a second, narrower PeachImage read this repo doesn't have today. WebP and AVIF are further
behind still — neither has any pass-through mechanism at all, so their embedded profiles (extractable
today via the existing `ImageMetadata.GetIccColorProfile()`, already used for JPEG) simply were never
wired into a PDF color space for those formats.

This gap already existed the moment #1085 shipped (which only ever addressed JPEG) but was never
formally tracked until #1086 revisited PNG embedding — see that change's migration note for the
user-visible framing.

Closing this needs, for PNG: a way to read the `iCCP` chunk without a full decode, wired into
`BuildDeviceOrIccColorSpace` the same way JPEG's profile is. For WebP/AVIF: decode normally (as today),
call `GetIccColorProfile()`, and wire the result in the same way.

Tracked as [issue #1106](https://github.com/jhaygood86/PeachPDF/issues/1106).
