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

## Unicode Character Database (text-processing data tables)

- **Location:** [`src/PeachPDF/Text/Resources/Bidi/`](src/PeachPDF/Text/Resources/Bidi/) — `DerivedBidiClass.txt.br`, `BidiBrackets.txt.br`, `BidiMirroring.txt.br` (consumed by `PeachPDF.Text.Bidi.BidiClassTable`/`BidiBrackets`/`BidiMirroring`); [`src/PeachPDF/Text/Resources/VerticalOrientation/`](src/PeachPDF/Text/Resources/VerticalOrientation/) — `VerticalOrientation.txt.br` (`PeachPDF.Text.VerticalOrientationTable`); [`src/PeachPDF/Text/Resources/Script/`](src/PeachPDF/Text/Resources/Script/) — `Scripts.txt.br` (`PeachPDF.Text.ScriptTable`); [`src/PeachPDF/Text/Resources/ArabicJoining/`](src/PeachPDF/Text/Resources/ArabicJoining/) — `DerivedJoiningType.txt.br` (`PeachPDF.Text.ArabicShapingTable`). All Brotli-compressed.
- **Upstream source:** the Unicode Character Database (UCD), version 17.0.0, mirrored unmodified at [`assets/unicode/DerivedBidiClass.txt`](assets/unicode/DerivedBidiClass.txt), [`assets/unicode/BidiBrackets.txt`](assets/unicode/BidiBrackets.txt), [`assets/unicode/BidiMirroring.txt`](assets/unicode/BidiMirroring.txt), [`assets/unicode/VerticalOrientation.txt`](assets/unicode/VerticalOrientation.txt), [`assets/unicode/Scripts.txt`](assets/unicode/Scripts.txt), [`assets/unicode/DerivedJoiningType.txt`](assets/unicode/DerivedJoiningType.txt) — see [`assets/unicode/UnicodeCharacterDatabase.LICENSE.txt`](assets/unicode/UnicodeCharacterDatabase.LICENSE.txt)
- **Generation scripts:** [`assets/unicode/generate_bidi_tables.py`](assets/unicode/generate_bidi_tables.py), [`assets/unicode/generate_vertical_orientation_table.py`](assets/unicode/generate_vertical_orientation_table.py), [`assets/unicode/generate_script_table.py`](assets/unicode/generate_script_table.py), [`assets/unicode/generate_arabic_joining_table.py`](assets/unicode/generate_arabic_joining_table.py) — each reparses its own UCD source file(s) above into the compact per-codepoint-range records the embedded resources actually ship
- **Also present, not shipped:** `assets/unicode/BidiCharacterTest.txt`, the Unicode Consortium's own bidi conformance test suite, used only by `PeachPDF.Tests` to verify the Unicode Bidirectional Algorithm (UAX #9) implementation against real test vectors; `assets/unicode/ArabicShaping.txt`, kept alongside `DerivedJoiningType.txt` as the normative source that file is itself derived from, for provenance — neither is ever embedded in the library or its NuGet package
- **License:** [Unicode License v3](https://www.unicode.org/license.txt) ("Unicode® License Agreement — Data Files and Software")

Each UCD source file carries this notice in its own header, reproduced here rather than the full license text (see the license file linked above for that):

> © 2025 Unicode®, Inc. Unicode and the Unicode Logo are registered trademarks of Unicode, Inc. in the U.S. and other countries. For terms of use and license, see https://www.unicode.org/terms_of_use.html

## HarfBuzz (ported Arabic/Syriac joining state machine)

- **Location:** [`src/PeachPDF/Text/Shaping/Arabic/ArabicJoiningStateTable.cs`](src/PeachPDF/Text/Shaping/Arabic/ArabicJoiningStateTable.cs), [`src/PeachPDF/Text/Shaping/Arabic/ArabicJoiningShaper.cs`](src/PeachPDF/Text/Shaping/Arabic/ArabicJoiningShaper.cs) — a line-by-line C# port of the cursive-joining state machine (`arabic_state_table`/`arabic_joining`), not merely inspired by it
- **Upstream source:** [HarfBuzz](https://github.com/harfbuzz/harfbuzz), `src/hb-ot-shaper-arabic.cc`, retrieved 2026-09-04 from the `main` branch
- **License:** the "Old MIT" license HarfBuzz is licensed under project-wide (see HarfBuzz's own [`COPYING`](https://github.com/harfbuzz/harfbuzz/blob/main/COPYING)) — functionally MIT-equivalent, reproduced in full below

Each ported file's header reproduces this exact notice (as it appeared in the original `hb-ot-shaper-arabic.cc`), plus a comment naming the specific upstream function it was ported from:

```
Copyright © 2010,2012  Google, Inc.

 This is part of HarfBuzz, a text shaping library.

Permission is hereby granted, without written agreement and without
license or royalty fees, to use, copy, modify, and distribute this
software and its documentation for any purpose, provided that the
above copyright notice and the following two paragraphs appear in
all copies of this software.

IN NO EVENT SHALL THE COPYRIGHT HOLDER BE LIABLE TO ANY PARTY FOR
DIRECT, INDIRECT, SPECIAL, INCIDENTAL, OR CONSEQUENTIAL DAMAGES
ARISING OUT OF THE USE OF THIS SOFTWARE AND ITS DOCUMENTATION, EVEN
IF THE COPYRIGHT HOLDER HAS BEEN ADVISED OF THE POSSIBILITY OF SUCH
DAMAGE.

THE COPYRIGHT HOLDER SPECIFICALLY DISCLAIMS ANY WARRANTIES, INCLUDING,
BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND
FITNESS FOR A PARTICULAR PURPOSE.  THE SOFTWARE PROVIDED HEREUNDER IS
ON AN "AS IS" BASIS, AND THE COPYRIGHT HOLDER HAS NO OBLIGATION TO
PROVIDE MAINTENANCE, SUPPORT, UPDATES, ENHANCEMENTS, OR MODIFICATIONS.

Google Author(s): Behdad Esfahbod
```

Everything else in `src/PeachPDF/Text/Shaping/Arabic/` (`ArabicJoiningForm.cs`) is original PeachPDF code (BSD 3-Clause, the project's own license) written to consume the ported state machine — not itself derived from HarfBuzz, so it carries no HarfBuzz notice.

## HarfBuzz (ported GPOS cursive attachment formula)

- **Location:** [`src/PeachPDF/Text/GposPositioner.cs`](src/PeachPDF/Text/GposPositioner.cs) — the `TryApplyCursivePair` method's main-direction (X) correction only (the surrounding dispatch/iteration and the cross-direction Y correction are original PeachPDF code)
- **Upstream source:** [HarfBuzz](https://github.com/harfbuzz/harfbuzz), `src/OT/Layout/GPOS/CursivePosFormat1.hh` (`CursivePosFormat1::apply`, the `HB_DIRECTION_RTL` branch of its main-direction adjustment), retrieved 2026-09-04 from the `main` branch
- **License:** the "Old MIT" license HarfBuzz is licensed under project-wide (see HarfBuzz's own [`COPYING`](https://github.com/harfbuzz/harfbuzz/blob/main/COPYING)) — functionally MIT-equivalent. This specific file carries no individual per-file header (true of most files under `src/OT/Layout/`), so the notice below is HarfBuzz's own project-wide one from `COPYING`, naming every contributor `COPYING` lists rather than one individual file's own (narrower) header:

```
Copyright © 2010-2022  Google, Inc.
Copyright © 2015-2020  Ebrahim Byagowi
Copyright © 2019,2020  Facebook, Inc.
Copyright © 2012,2015  Mozilla Foundation
Copyright © 2011  Codethink Limited
Copyright © 2008,2010  Nokia Corporation and/or its subsidiary(-ies)
Copyright © 2009  Keith Stribley
Copyright © 2011  Martin Hosken and SIL International
Copyright © 2007  Chris Wilson
Copyright © 2005,2006,2020,2021,2022,2023  Behdad Esfahbod
Copyright © 2004,2007,2008,2009,2010,2013,2021,2022,2023  Red Hat, Inc.
Copyright © 1998-2005  David Turner and Werner Lemberg
Copyright © 2016  Igalia S.L.
Copyright © 2022  Matthias Clasen
Copyright © 2018,2021  Khaled Hosny
Copyright © 2018,2019,2020  Adobe, Inc
Copyright © 2013-2015  Alexei Podtelezhnikov

For full copyright notices consult the individual files in the package.

Permission is hereby granted, without written agreement and without
license or royalty fees, to use, copy, modify, and distribute this
software and its documentation for any purpose, provided that the
above copyright notice and the following two paragraphs appear in
all copies of this software.

IN NO EVENT SHALL THE COPYRIGHT HOLDER BE LIABLE TO ANY PARTY FOR
DIRECT, INDIRECT, SPECIAL, INCIDENTAL, OR CONSEQUENTIAL DAMAGES
ARISING OUT OF THE USE OF THIS SOFTWARE AND ITS DOCUMENTATION, EVEN
IF THE COPYRIGHT HOLDER HAS BEEN ADVISED OF THE POSSIBILITY OF SUCH
DAMAGE.

THE COPYRIGHT HOLDER SPECIFICALLY DISCLAIMS ANY WARRANTIES, INCLUDING,
BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND
FITNESS FOR A PARTICULAR PURPOSE.  THE SOFTWARE PROVIDED HEREUNDER IS
ON AN "AS IS" BASIS, AND THE COPYRIGHT HOLDER HAS NO OBLIGATION TO
PROVIDE MAINTENANCE, SUPPORT, UPDATES, ENHANCEMENTS, OR MODIFICATIONS.
```

A first implementation of this method derived its own formula directly from the OpenType spec's prose
instead of porting HarfBuzz's real algorithm, and was wrong against a real font (see this fix's own
recent-fixes entry) — replaced with this direct port once the divergence was found.

## HarfBuzz (ported Universal Shaping Engine algorithm)

- **Location:** [`src/PeachPDF/Text/Shaping/Use/UseSyllableScanner.cs`](src/PeachPDF/Text/Shaping/Use/UseSyllableScanner.cs) (a hand-written scanner implementing the same grammar as the ported `.rl` source below, standing in for HarfBuzz's own Ragel-generated state machine since this repo has no Ragel toolchain) and [`src/PeachPDF/Text/Shaping/Use/UseReorderer.cs`](src/PeachPDF/Text/Shaping/Use/UseReorderer.cs) (`ReorderSyllable`, a line-by-line port of `reorder_syllable_use`)
- **Upstream source:** [HarfBuzz](https://github.com/harfbuzz/harfbuzz) — the syllable grammar from `src/hb-ot-shaper-use-machine.rl`, the reorder algorithm from `src/hb-ot-shaper-use.cc` (`reorder_syllable_use`), both retrieved 2026-09-05 from the `main` branch
- **License:** the "Old MIT" license HarfBuzz is licensed under project-wide (see HarfBuzz's own [`COPYING`](https://github.com/harfbuzz/harfbuzz/blob/main/COPYING)) — functionally MIT-equivalent, reproduced below

Both upstream files carry the same header notice, reproduced in each ported file:

```
Copyright © 2015  Mozilla Foundation.
Copyright © 2015  Google, Inc.

 This is part of HarfBuzz, a text shaping library.

Permission is hereby granted, without written agreement and without
license or royalty fees, to use, copy, modify, and distribute this
software and its documentation for any purpose, provided that the
above copyright notice and the following two paragraphs appear in
all copies of this software.

IN NO EVENT SHALL THE COPYRIGHT HOLDER BE LIABLE TO ANY PARTY FOR
DIRECT, INDIRECT, SPECIAL, INCIDENTAL, OR CONSEQUENTIAL DAMAGES
ARISING OUT OF THE USE OF THIS SOFTWARE AND ITS DOCUMENTATION, EVEN
IF THE COPYRIGHT HOLDER HAS BEEN ADVISED OF THE POSSIBILITY OF SUCH
DAMAGE.

THE COPYRIGHT HOLDER SPECIFICALLY DISCLAIMS ANY WARRANTIES, INCLUDING,
BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND
FITNESS FOR A PARTICULAR PURPOSE.  THE SOFTWARE PROVIDED HEREUNDER IS
ON AN "AS IS" BASIS, AND THE COPYRIGHT HOLDER HAS NO OBLIGATION TO
PROVIDE MAINTENANCE, SUPPORT, UPDATES, ENHANCEMENTS, OR MODIFICATIONS.

Mozilla Author(s): Jonathan Kew
Google Author(s): Behdad Esfahbod
```

`UseCategoryClassifier.cs` ports the category-derivation *algorithm* from HarfBuzz's build-time
generator script (`src/gen-use-table.py`, same upstream project/license) rather than any single
runtime `.cc`/`.hh` file — that script's own predicates are what this class's
`is_BASE`/`is_VOWEL`/etc.-equivalent branches are ported from, since the runtime category lookup
itself (`hb-ot-shaper-use-table.hh`) is a bit-packed lookup trie with no human-readable per-codepoint
mapping to port from directly (see that class's own remarks). `gen-use-table.py` carries no individual
per-file header (true of most build-time/tooling scripts under `src/`), so - same as the cursive
attachment port above - it falls under HarfBuzz's own project-wide notice from `COPYING`, naming every
contributor `COPYING` lists rather than one individual file's own (narrower) header; see that section
above for the full text (identical here). Everything else in `src/PeachPDF/Text/Shaping/Use/`
(`UseCategory.cs`, `UseSyllableType.cs`, `UseSyllable.cs`) is original PeachPDF code (BSD 3-Clause, the
project's own license) written to consume the ported algorithm — not itself derived from HarfBuzz, so
it carries no HarfBuzz notice.

## Bundled font assets

- **Location:** [`assets/fonts/`](assets/fonts/) — shared by the test suite, the showcase harness, and the browser demo; each font's own license notice sits beside it as a `.LICENSE.txt`
- **License:** varies per font (SIL OFL 1.1 or 3-Clause BSD)

None of these fonts ships in the PeachPDF library or its NuGet package. See [docs/license.md's "Font assets" table](docs/license.md#font-assets) for the full list, what each one is used for, and its specific license.

## Hyphenation patterns (hyph-utf8 / CTAN)

- **Location:** [`src/PeachPDF/Text/Resources/Patterns/`](src/PeachPDF/Text/Resources/Patterns/) — one Brotli-compressed `hyph-<tag>.txt.br` file per language (73 files)
- **Upstream source:** the `hyph-utf8` package from CTAN, mirrored at [github.com/hyphenation/tex-hyphen](https://github.com/hyphenation/tex-hyphen)
- **Regeneration/provenance script:** [`tools/Update-HyphenationPatterns.ps1`](tools/Update-HyphenationPatterns.ps1), pinned to a specific upstream commit for reproducibility

Each language's original `hyph-<tag>.tex` source carries its own copyright and license notice (these patterns are contributed independently, by different authors, over several decades). `tools/Update-HyphenationPatterns.ps1` bundles **only permissively-licensed pattern sets** (MIT/LPPL/BSD-style/public-domain) and skips any whose resolved license is GPL/LGPL-family or unstated (see the script's `Test-PermissiveLicense` function) — consistent with PeachPDF's own BSD 3-Clause license. Each compressed pattern file also carries this same notice inline in its decompressed text header, alongside its title, copyright holder, and a source/retrieval-date/commit stamp.

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
