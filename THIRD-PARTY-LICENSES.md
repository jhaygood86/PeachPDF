# Third-Party Licenses

PeachPDF is MIT-licensed (see [LICENSE](LICENSE)), but it embeds and adapts a small number of third-party components directly in its source tree, each carrying its own license terms. This document collects those licenses, alongside where each component lives in the repo, so they aren't just scattered `LICENSE`/`license.txt` files a reader has to go hunting for.

## PdfSharpCore (embedded fork)

- **Location:** [`src/PeachPDF/PdfSharpCore/`](src/PeachPDF/PdfSharpCore/)
- **License file:** [`src/PeachPDF/PdfSharpCore/LICENSE.md`](src/PeachPDF/PdfSharpCore/LICENSE.md)
- **License:** MIT

```
## MIT License

Copyright (c) 2001-2024 empira Software GmbH, Troisdorf (Cologne Area), Germany
Copyright (c) 2017-2025 Justin Haygood

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

## Hyphenation patterns (hyph-utf8 / CTAN)

- **Location:** [`src/PeachPDF/Text/Resources/Patterns/`](src/PeachPDF/Text/Resources/Patterns/) — one Brotli-compressed `hyph-<tag>.txt.br` file per language (73 files)
- **Upstream source:** the `hyph-utf8` package from CTAN, mirrored at [github.com/hyphenation/tex-hyphen](https://github.com/hyphenation/tex-hyphen)
- **Regeneration/provenance script:** [`tools/Update-HyphenationPatterns.ps1`](tools/Update-HyphenationPatterns.ps1), pinned to a specific upstream commit for reproducibility

Each language's original `hyph-<tag>.tex` source carries its own copyright and license notice (these patterns are contributed independently, by different authors, over several decades). `tools/Update-HyphenationPatterns.ps1` bundles **only permissively-licensed pattern sets** (MIT/LPPL/BSD-style/public-domain) and skips any whose resolved license is GPL/LGPL-family or unstated (see the script's `Test-PermissiveLicense` function) — consistent with PeachPDF's own MIT license. Each compressed pattern file also carries this same notice inline in its decompressed text header, alongside its title, copyright holder, and a source/retrieval-date/commit stamp.

The table below groups the 73 bundled languages by their exact license text, so each distinct notice is reproduced once rather than 73 times. Language tags correspond to `hyph-<tag>.txt.br` in the Patterns directory above.

> This is a point-in-time snapshot of what the pinned upstream commit contained when generated. If `tools/Update-HyphenationPatterns.ps1` is re-run against a newer commit, upstream files may have changed license text (or license status), and this section should be regenerated to match.

### MIT (standard boilerplate)

26 languages: `as, be, bn, cu, cy, da, et, fr, fur, ga, gu, hi, it, kn, la-x-classic, la-x-liturgic, lt, ml, mn-cyrl, mr, pms, rm, sl, ta, te, tk`

> Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions: The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software. THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

### MIT (standard boilerplate, typographic quotation marks around "AS IS" only)

2 languages: `af, es`

> Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions: The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software. THE SOFTWARE IS PROVIDED “AS IS”, WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

### MIT (standard boilerplate, typographic quotation marks)

12 languages: `cop, de-1901, de-1996, de-ch-1901, en-gb, la, oc, or, pa, pi, sq, zh-latn-pinyin`

> Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the “Software”), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions: The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software. THE SOFTWARE IS PROVIDED “AS IS”, WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

### MIT (by reference)

6 languages: `el-monoton, el-polyton, fi-x-school, grc, ka, nl`

> MIT — https://opensource.org/licenses/MIT

1 language: `sk`

> MIT — http://www.opensource.org/licenses/MIT

1 language: `mul-ethi`

> This file is available under the terms of the MIT licence. Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the “Software”), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions: The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software. THE SOFTWARE IS PROVIDED “AS IS”, WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

### LaTeX Project Public License (LPPL)

4 languages: `ca, eo, sh-cyrl, sh-latn`

> LPPL 1 or later — https://latex-project.org/lppl/

1 language: `tr`

> LPPL 1 or later — https://latex-project.org/lppl/lppl-1-0.html

1 language: `uk`

> LPPL — https://latex-project.org/lppl/

1 language: `sv`

> LPPL 1.2 or later

1 language: `ru`

> LPPL 1.2 or later — https://latex-project.org/lppl/

1 language: `is`

> LPPL 1.2 or later — http://www.latex-project.org/lppl.txt

1 language: `ia`

> LPPL 1.3 — https://latex-project.org/lppl/

1 language: `kmr`

> LPPL 1.3 — https://latex-project.org/lppl/lppl-1-3.html

1 language: `th`

> LPPL 1.3 or later — https://latex-project.org/lppl/

1 language: `hsb`

> LPPL 1.3 or later — http://www.latex-project.org/lppl.txt

### BSD-style "Data Files" license

2 languages: `eu, hr`

> Permission is hereby granted, free of charge, to any person obtaining a copy of this file and any associated documentation (the "Data Files") to deal in the Data Files without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, and/or sell copies of the Data Files, and to permit persons to whom the Data Files are furnished to do so, provided that (a) this copyright and permission notice appear with all copies of the Data Files, (b) this copyright and permission notice appear in associated documentation, and (c) there is clear notice in each modified Data File as well as in the documentation associated with the Data File(s) that the data has been modified. THE DATA FILES ARE PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT OF THIRD PARTY RIGHTS. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR HOLDERS INCLUDED IN THIS NOTICE BE LIABLE FOR ANY CLAIM, OR ANY SPECIAL INDIRECT OR CONSEQUENTIAL DAMAGES, OR ANY DAMAGES WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER IN AN ACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF OR IN CONNECTION WITH THE USE OR PERFORMANCE OF THE DATA FILES. Except as contained in this notice, the name of a copyright holder shall not be used in advertising or otherwise to promote the sale, use or other dealings in these Data Files without prior written authorization of the copyright holder.

1 language: `pt`

> Redistribution and use in source and binary forms, with or without modification, are permitted provided that the following conditions are met: * Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. * Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following disclaimer in the documentation and/or other materials provided with the distribution. * Neither the name of the University of Campinas, of the University of Minho nor the names of its contributors may be used to endorse or promote products derived from this software without specific prior written permission. THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL PEDRO J. DE REZENDE OR J.JOAO DIAS ALMEIDA BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

1 language: `bg`

> This software may be used, modified, copied, distributed, and sold, both in source and binary form provided that the above copyright notice and these terms are retained. The name of the author may not be used to endorse or promote products derived from this software without prior permission. THIS SOFTWARE IS PROVIDES "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES ARE DISCLAIMED. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR ANY DAMAGES ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE.

### Public domain / unlicensed / freely-distributable

2 languages: `nb, nn`

> Copying and distribution of this file, with or without modification, are permitted in any medium without royalty, provided the copyright notice and this notice are preserved.

1 language: `en-us`

> Copying and distribution of this file, with or without modification, are permitted in any medium without royalty provided the copyright notice and this notice are preserved.

1 language: `pl`

> This macro file belongs to the public domain under the conditions specified by the author of TeX: “Macro files like PLAIN.TEX should not be changed in any way, except with respect to preloaded fonts, unless the changes are authorized by the authors of the macros.” — Donald E. Knuth

1 language: `kk`

> Public domain

1 language: `fi`

> Patterns may be freely distributed

1 language: `gl`

> Unlicence — https://unlicense.org/

1 language: `sa`

> You may freely use, copy, modify and/or distribute this file.
