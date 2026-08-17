# TGA, PSD, and HDR raster images no longer decode on either target framework

Previously (through the released v0.9.12): raster image decoding was handled by StbImageSharp on both
.NET 8 and .NET 10, which supports JPEG, PNG, BMP, GIF, TGA, PSD, and HDR. An `<img>`,
`background-image`, or SVG `<image>` pointing at any of those formats decoded and rendered normally.

Now: raster decoding runs entirely through the PeachImage package, which implements JPEG, PNG, BMP, and
GIF only. A document that embeds a TGA, PSD, or HDR image renders that one image as an unresolved
replaced element (the same as any other undecodable/corrupt image — nothing crashes) rather than
decoding it, on every target framework. JPEG/PNG/BMP/GIF decoding, and encoding output (JPEG/BMP
embedding into the PDF), are unaffected. See
[tga-psd-hdr-unsupported](../accepted-gaps/tga-psd-hdr-unsupported.md) for the full accepted-gap
writeup.
