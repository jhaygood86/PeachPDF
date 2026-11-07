# PeachDrawing.Text.Data

The Unicode, hyphenation and dictionary data [PeachDrawing.Text](https://www.nuget.org/packages/PeachDrawing.Text/) reads at
runtime: the Bidi/Script/Use/VerticalOrientation/ArabicJoining tables derived from the Unicode Character Database, TeX
hyphenation patterns, OpenType/BCP 47 language tags, and the Thai/Lao/Khmer/Burmese word lists line breaking uses.

This is an implementation-detail package of PeachDrawing.Text, split out on its own so the data is embedded once rather
than once per target framework PeachDrawing.Text builds for (NuGet does not merge a resource across a package's
`lib/<tfm>` folders). It has no dependencies of its own, exposes no public API, and is not meant to be referenced
directly - add a package reference to `PeachDrawing.Text` (or `PeachPDF`) instead, and this comes along with it.

## Using it

You do not use this package directly; install the package that needs it:

```bash
dotnet add package PeachDrawing.Text     # fonts, shaping, layout, bidi, line breaking, hyphenation
dotnet add package PeachPDF              # HTML and CSS to PDF; brings PeachDrawing.Text with it
```

The data is read once, on first use, and cached for the life of the process. All of it is Brotli-compressed; on a host whose Brotli decoder does not work (WebAssembly in a
browser) register a managed one with `PeachDrawing.Text.Compression.BrotliDecompression.SetDecompressor(...)` before using a feature backed by it, or those features degrade
(hyphenation, Thai/Lao/Khmer/Burmese line breaking and the Unicode tables) rather than fail. It targets `netstandard2.0`, and is versioned in lockstep with PeachPDF.

See [PeachDrawing.Text's own README](https://www.nuget.org/packages/PeachDrawing.Text/) and
[peachpdf.net](https://peachpdf.net/) for what the engine and PeachPDF itself do.

## Sponsorship

PeachPDF is free and open source. If it saves you time, you can [sponsor jhaygood86 on GitHub](https://github.com/sponsors/jhaygood86);
sponsors can get paid support, see [Sponsorship](https://peachpdf.net/sponsorship.html).

## Licenses

The data in this package carries real license obligations of its own (the Unicode Character Database, ICU's break-iterator
word lists, and the `hyph-utf8` hyphenation patterns) - see [THIRD-PARTY-LICENSES.md](THIRD-PARTY-LICENSES.md).
