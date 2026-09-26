# Third-Party Licenses

PeachPDF is BSD 3-Clause-licensed (see [LICENSE](LICENSE)), but it embeds and adapts a small number of third-party components directly in its source tree, each carrying its own license terms. This document collects those licenses, alongside where each component lives in the repo, so they aren't just scattered `LICENSE`/`license.txt` files a reader has to go hunting for.

## PdfSharpCore (embedded fork)

- **Location:** [`src/PeachPDF/PdfSharpCore/`](src/PeachPDF/PdfSharpCore/)
- **License file:** [`src/PeachPDF/PdfSharpCore/LICENSE.md`](src/PeachPDF/PdfSharpCore/LICENSE.md)
- **License:** MIT

```
## MIT License

Copyright (c) 2001-2024 empira Software GmbH, Troisdorf (Cologne Area), Germany
Copyright (c) 2017-2026 Justin Haygood

http://docs.pdfsharp.net

MIT License

Permission is hereby granted, free of charge, to any person obtaining a
copy of this software and associated documentation files (the "Software"),
to deal in the Software without restriction, including without limitation
the rights to use, copy, modify, merge, publish, distribute, sublicense,
and/or sell copies of the Software, and to permit persons to whom the
Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included
in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
DEALINGS IN THE SOFTWARE.
```

## ExCSS (CSS parser, adapted in-tree)

- **Location:** [`src/PeachPDF/CSS/`](src/PeachPDF/CSS/)
- **License file:** [`src/PeachPDF/CSS/license.txt`](src/PeachPDF/CSS/license.txt)
- **License:** MIT

```
The MIT License (MIT)

Copyright (c) 2024 Tyler Brinks

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## Bundled font assets

- **Location:** [`assets/fonts/`](assets/fonts/) — shared by the test suite, the showcase harness, and the browser demo; each font's own license notice sits beside it as a `.LICENSE.txt`
- **License:** varies per font (SIL OFL 1.1, 3-Clause BSD, or CC0 for the hand-authored test fixtures)

None of these fonts ships in the PeachPDF library or its NuGet package. See [docs/license.md's "Font assets" table](docs/license.md#font-assets) for the full list, what each one is used for, and its specific license.

## PeachDrawing.Text

The font and text engine lives in its own package, [`PeachDrawing.Text`](src/PeachDrawing.Text/). The third-party components in it (PDFsharp-derived font readers, the ported HarfBuzz algorithms, the Unicode Character Database tables and the hyphenation patterns) are documented in [`src/PeachDrawing.Text/THIRD-PARTY-LICENSES.md`](src/PeachDrawing.Text/THIRD-PARTY-LICENSES.md), which ships inside that package and is printed after this document by the `peachpdf` command-line tool's `--credits`.
