---
layout: default
---

# License

Usage of PeachPDF is **free and open source** under the terms of the **BSD 3-Clause license**. There is no cost, no royalty, and no separate commercial license — the same terms apply to everyone, whether or not you have a [paid support plan](support.md) or [sponsor the project](sponsorship.md).

## License text

```
Copyright (c) 2009, José Manuel Menéndez Poo
Copyright (c) 2013, Arthur Teplitzki
Copyright (c) 2017-2025 Justin Haygood

All rights reserved.

Redistribution and use in source and binary forms, with or without modification,
are permitted provided that the following conditions are met:

  Redistributions of source code must retain the above copyright notice, this
  list of conditions and the following disclaimer.

  Redistributions in binary form must reproduce the above copyright notice, this
  list of conditions and the following disclaimer in the documentation and/or
  other materials provided with the distribution.

  Neither the name of the menendezpoo.com, ArthurHub nor the names of its
  contributors may be used to endorse or promote products derived from
  this software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR
ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON
ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```

## Third-party components

Portions of PeachPDF are sourced from third parties and are licensed under alternative, BSD-compatible permissive license terms:

| Functionality | Origin | License |
|---|---|---|
| CSS engine (parsing, CSS-OM) | Fork of [ExCSS](https://github.com/TylerBrinks/ExCSS) (Tyler Brinks) | [MIT](https://github.com/jhaygood86/PeachPDF/blob/main/src/PeachPDF/CSS/license.txt) |
| PDF engine (document model, PDF writing) | Fork of [PDFsharp](https://github.com/empira/PDFsharp) (empira Software GmbH) | [MIT](https://github.com/jhaygood86/PeachPDF/blob/main/src/PeachPDF/PdfSharpCore/LICENSE.md) |
| Core HTML rendering engine lineage | Derived from [HtmlRenderer](https://github.com/ArthurHub/HTML-Renderer) (José Manuel Menéndez Poo, Arthur Teplitzki) | BSD 3-Clause (the license above) |
| Hyphenation pattern data (`hyphens: auto`) | CTAN's [hyph-utf8](https://ctan.org/pkg/hyph-utf8) package | A mix of permissive licenses including MIT and LPPL, plus BSD-style and public-domain patterns. Pattern files under copyleft (GPL/LGPL) or missing license terms are deliberately **not** shipped — see [`hyphens: auto` language coverage](html-css-support.md#hyphens-auto-language-coverage) |

### Test-suite font assets (not distributed)

The following fonts are embedded in the `PeachPDF.Tests` project **solely as test assets** — so the test suite can exercise font matching, `unicode-range` selection, subsetting/embedding, monochrome-emoji (astral / `cmap` format-12), and color-glyph (`COLR`/`CPAL`) rendering against real font files rather than depending on whatever fonts happen to be installed on the machine running the tests. They are **not shipped in the PeachPDF library or its NuGet package**, and impose no obligation on applications that consume PeachPDF. The OFL-licensed fonts are each under the [SIL Open Font License 1.1](https://openfontlicense.org/):

| Font | License |
|---|---|
| Noto Emoji (subset; © 2013 Google LLC) — see `src/PeachPDF.Tests/NotoEmoji-Regular.LICENSE.txt` | [SIL OFL 1.1](https://openfontlicense.org/) |
| Noto Color Emoji (COLR v1 subset; © 2013 Google LLC) — see `src/PeachPDF.Tests/NotoColorEmoji-Subset.LICENSE.txt` | [SIL OFL 1.1](https://openfontlicense.org/) |
| Nabla (COLR v1 subset; © 2022 The Nabla Project Authors) — a 7-palette color font for `font-palette` tests; see `src/PeachPDF.Tests/NablaSubset.LICENSE.txt` | [SIL OFL 1.1](https://openfontlicense.org/) |
| Inter | [SIL OFL 1.1](https://openfontlicense.org/) |
| Source Code Pro | [SIL OFL 1.1](https://openfontlicense.org/) |
| Source Sans 3 | [SIL OFL 1.1](https://openfontlicense.org/) |

The two hand-authored `COLR` color-glyph fixtures used by the color-font tests (`ColorTestV0.ttf` / `ColorTestV1.ttf`) contain no third-party font data and are released into the public domain (CC0); see `src/PeachPDF.Tests/TestSupport/Fonts/ColorTestFonts.LICENSE.txt`.

### Showcase assets (not distributed)

The following assets are embedded in the `PeachPDF.TestHarness` project **solely to render a showcase** — they are **not shipped in the PeachPDF library or its NuGet package**, and impose no obligation on applications that consume PeachPDF. Each original license notice is kept intact in the file header and/or an accompanying `LICENSE.txt`:

| Asset | License |
|---|---|
| [Charts.css](https://chartscss.org) v1.2.0 (© 2020 Rami Yushuvaev) — the pure-CSS charting framework used by the [Charts.css showcase](showcase.html); see `src/PeachPDF.TestHarness/charts.css.LICENSE.txt` | [MIT](https://github.com/jhaygood86/PeachPDF/blob/main/src/PeachPDF.TestHarness/charts.css.LICENSE.txt) |
| Noto Color Emoji (COLR v1 subset; © 2013 Google LLC) — the real color-emoji font in the [Color Fonts showcase](showcase.html); see `src/PeachPDF.TestHarness/NotoColorEmoji-Subset.LICENSE.txt` | [SIL OFL 1.1](https://openfontlicense.org/) |
| Nabla (COLR v1 subset; © 2022 The Nabla Project Authors) — the 7-palette color font in the [font-palette showcase](showcase.html); see `src/PeachPDF.TestHarness/NablaSubset.LICENSE.txt` | [SIL OFL 1.1](https://openfontlicense.org/) |

## License FAQ

### Can I use PeachPDF in a commercial product?

Yes. The BSD 3-Clause license permits commercial use with no fees or royalties. Closed-source, SaaS, internal tooling, shipped desktop software — all fine.

### Do I have to open-source my application?

No. BSD 3-Clause is a *permissive* license, not a copyleft one. Using, linking, or embedding PeachPDF places no requirements on your own code's license.

### What am I required to do?

If you redistribute PeachPDF's source code, keep the copyright notice, conditions, and disclaimer intact. If you redistribute it in binary form (which includes shipping an application that bundles it), reproduce the copyright notice, conditions, and disclaimer in your documentation and/or other distribution materials. Consuming the unmodified [NuGet package](https://www.nuget.org/packages/PeachPDF) already carries the project's license notices with it; most applications satisfy the binary-form requirement with an ordinary third-party-notices file or "about" screen entry.

### Can I modify PeachPDF or maintain my own fork?

Yes — modification and redistribution are explicitly permitted, under the same notice conditions above. Contributions back upstream are welcome but never required.

### Can I use the authors' names to promote my product?

No. The third clause forbids using the names of the copyright holders or contributors to endorse or promote derived products without prior written permission. Saying your product *uses* PeachPDF is fine; implying the authors *endorse* your product is not.

### Is there a warranty?

No — the software is provided "as is", without warranty of any kind, and the copyright holders are not liable for damages arising from its use. If you need guaranteed help, response times, or integration assistance, that's exactly what the [paid support plan](support.md#paid-support) is for.

### Does buying paid support or sponsoring change the license?

No. Support and [sponsorship](sponsorship.md) buy help and priority, not different license terms. Everyone uses PeachPDF under the same BSD 3-Clause license.
