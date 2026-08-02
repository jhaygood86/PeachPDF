---
layout: default
---

# License

Usage of PeachPDF is **free and open source** under the terms of the **BSD 3-Clause license**. There is no cost, no royalty, and no separate commercial license — the same terms apply to everyone, whether or not you have a [paid support plan](support.md) or [sponsor the project](sponsorship.md).

## License text

```
Copyright (c) 2009, José Manuel Menéndez Poo
Copyright (c) 2013, Arthur Teplitzki
Copyright (c) 2017-2026 Justin Haygood

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
| Bidi data tables (Bidi_Class, bracket pairs, mirroring — the Unicode Bidi Algorithm, UAX #9) | The [Unicode Character Database](https://www.unicode.org/ucd/), version 17.0.0 | [Unicode License v3](https://www.unicode.org/license.txt) |

### Font assets

Every font the repository bundles lives in one place — the `assets/fonts/` directory — shared by the test suite, the showcase harness and the browser demo, with each font's original license notice kept intact beside it in an accompanying `.LICENSE.txt`.

**None of these fonts is shipped in the PeachPDF library or its NuGet package**, and none imposes any obligation on applications that consume PeachPDF. They exist so the test suite can exercise font matching, `unicode-range` selection, subsetting/embedding, monochrome-emoji (astral / `cmap` format-12) and color-glyph (`COLR`/`CPAL`) rendering against real font files instead of depending on whatever happens to be installed on the machine running the tests; so the showcase can demonstrate color fonts; and — for the Liberation family only — so the [in-browser demo](getting-started.md) has fonts at all, since a WebAssembly host has no discoverable system fonts. The Liberation fonts are the one exception to "not distributed": they are served as static assets by the demo page.

| Font | Used by | License |
|---|---|---|
| Liberation Sans, Liberation Serif, Liberation Mono 2.1.5 (© 2012 Red Hat, Inc.) — see `assets/fonts/LiberationFonts.LICENSE.txt` | Browser demo, showcase | [SIL OFL 1.1](https://openfontlicense.org/) |
| Noto Emoji (subset; © 2013 Google LLC) — see `assets/fonts/NotoEmoji-Regular.LICENSE.txt` | Tests | [SIL OFL 1.1](https://openfontlicense.org/) |
| Noto Color Emoji (COLR v1 subset; © 2013 Google LLC) — the real color-emoji font in the [Color Fonts showcase](showcase.html); see `assets/fonts/NotoColorEmoji-Subset.LICENSE.txt` | Tests, showcase | [SIL OFL 1.1](https://openfontlicense.org/) |
| Nabla (COLR v1 subset; © 2022 The Nabla Project Authors) — the 7-palette color font behind `font-palette` support and the [font-palette showcase](showcase.html); see `assets/fonts/NablaSubset.LICENSE.txt` | Tests, showcase | [SIL OFL 1.1](https://openfontlicense.org/) |
| Noto Sans Hebrew (subset; © The Noto Project Authors) — real Hebrew glyphs for the [bidirectional text showcase](showcase.html); see `assets/fonts/NotoSansHebrewSubset.LICENSE.txt` | Tests, showcase | [SIL OFL 1.1](https://openfontlicense.org/) |
| Inter | Tests | [SIL OFL 1.1](https://openfontlicense.org/) |
| Source Code Pro | Tests | [SIL OFL 1.1](https://openfontlicense.org/) |
| Source Sans 3 | Tests | [SIL OFL 1.1](https://openfontlicense.org/) |
| gsubtest-lookup3 (synthetic GSUB conformance font; © web-platform-tests contributors) — the only publicly available font found with real petite-caps/all-petite-caps GSUB Alternate Substitution data; see `assets/fonts/gsubtest-lookup3.LICENSE.txt` | Tests | [3-Clause BSD](https://github.com/web-platform-tests/wpt/blob/master/LICENSE.md) |

The two hand-authored `COLR` color-glyph fixtures used by the color-font tests (`ColorTestV0.ttf` / `ColorTestV1.ttf`) contain no third-party font data and are released into the public domain (CC0); see `assets/fonts/ColorTestFonts.LICENSE.txt`.

**A note on the Liberation WOFF files.** The bundled Liberation fonts are format-converted from the upstream TrueType release ([liberation-fonts 2.1.5](https://github.com/liberationfonts/liberation-fonts)) to WOFF 1.0, which roughly halves the demo's download. The Liberation license declares a Reserved Font Name, and the OFL's definition of a "Modified Version" includes changing formats — so under [the SIL OFL FAQ](https://openfontlicense.org/documents/OFL-FAQ.txt) §2.2.1 the name may only be retained when the original font data is unchanged apart from WOFF compression and no conflicting WOFF metadata is added. The conversion satisfies both conditions: it is a lossless recompression with no subsetting and no WOFF metadata block, and it is verified after the fact — outlines, metrics, character mapping and the copyright/license name records are all compared against the source. The script that performs and checks it is `assets/fonts/convert_liberation_webfonts.py`, which also explains why the format is WOFF 1.0 rather than the smaller WOFF2.

### Showcase assets (not distributed)

The following non-font assets are embedded in the `PeachPDF.TestHarness` project **solely to render a showcase** — they are **not shipped in the PeachPDF library or its NuGet package**, and impose no obligation on applications that consume PeachPDF. Each original license notice is kept intact in the file header and/or an accompanying `LICENSE.txt`:

| Asset | License |
|---|---|
| [Charts.css](https://chartscss.org) v1.2.0 (© 2020 Rami Yushuvaev) — the pure-CSS charting framework used by the [Charts.css showcase](showcase.html); see `src/PeachPDF.TestHarness/charts.css.LICENSE.txt` | [MIT](https://github.com/jhaygood86/PeachPDF/blob/main/src/PeachPDF.TestHarness/charts.css.LICENSE.txt) |

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
