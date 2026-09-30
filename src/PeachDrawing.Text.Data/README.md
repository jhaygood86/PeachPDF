# PeachDrawing.Text.Data

The Unicode, hyphenation and dictionary data [PeachDrawing.Text](https://www.nuget.org/packages/PeachDrawing.Text/) reads at
runtime: the Bidi/Script/Use/VerticalOrientation/ArabicJoining tables derived from the Unicode Character Database, TeX
hyphenation patterns, OpenType/BCP 47 language tags, and the Thai/Lao/Khmer/Burmese word lists line breaking uses.

This is an implementation-detail package of PeachDrawing.Text, split out on its own so the data is embedded once rather
than once per target framework PeachDrawing.Text builds for (NuGet does not merge a resource across a package's
`lib/<tfm>` folders). It has no dependencies of its own, exposes no public API, and is not meant to be referenced
directly - add a package reference to `PeachDrawing.Text` (or `PeachPDF`) instead, and this comes along with it.

See [PeachDrawing.Text's own README](https://www.nuget.org/packages/PeachDrawing.Text/) and
[peachpdf.net](https://peachpdf.net/) for what the engine and PeachPDF itself do.

## Licenses

The data in this package carries real license obligations of its own (the Unicode Character Database, ICU's break-iterator
word lists, and the `hyph-utf8` hyphenation patterns) - see [THIRD-PARTY-LICENSES.md](THIRD-PARTY-LICENSES.md).
