# TGA, PSD, and HDR raster images are not decodable

`PdfSharpCore/Utils/PeachImageSource.cs` decodes raster images via the PeachImage NuGet package.
PeachImage implements JPEG, PNG, BMP, and GIF; TGA, PSD, and HDR are not implemented (see its own
README's Status section) and are not on its roadmap as of writing. A `<img>`/`background-image`/SVG
`<image>` pointing at a `.tga`/`.psd`/`.hdr` file (or the matching `data:` URI) throws
`InvalidOperationException` during decode (`PeachImageSource.Decode` normalizes PeachImage's
`ImageFormatException` to that type to match the existing non-fatal decode-failure contract
`ImageLoadHandler.LoadImageFromStream` and `SvgTreeBuilder.DecodeRasterImage` already swallow) instead
of rendering the image - the same graceful degradation any unrecognized/corrupt image already gets,
not a crash.

This used to work: `StbImageSharp`, the codec PeachPDF used before switching entirely to PeachImage
(see `.claude/recent-fixes/2026-08-15-net10-image-codec-ported-to-peachimage.md`), decodes TGA and HDR
natively (via stb_image's underlying C library) and PSD as well. The switch to PeachImage was made
across both target frameworks once PeachImage 0.2.0 added a net8.0 target alongside net10.0, so this
gap is no longer TFM-specific the way the (now-closed) GIF gap briefly was - TGA/PSD/HDR decoding is
gone on every PeachPDF build, not narrowed to one target framework.

`MimeTypeResolver`'s built-in MIME-type fallback map still lists `tga`/`psd`/`hdr` extensions (it only
resolves a MIME type string, unrelated to decode capability - see `docs/architecture.md`'s "Image
loading and decoding" section), so a `.tga` file still gets a correct `Content-Type` header; it just
can't be decoded once fetched.

Closing this gap means PeachImage adding codecs for these formats (unlikely to be prioritized - they're
comparatively rare on the web PeachImage targets) or PeachPDF keeping some other TGA/PSD/HDR-capable
dependency around for just these three formats, which would reintroduce exactly the two-codec
complexity this port was meant to eliminate. Revisit if a real user need for one of these formats shows
up.
