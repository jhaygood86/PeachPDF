# Third-Party Licenses (PeachDrawing.Text.Data)

PeachDrawing.Text.Data is BSD 3-Clause-licensed (see [LICENSE](../../LICENSE)), the same as PeachDrawing.Text and PeachPDF, but the data it embeds carries its own, separate license terms. This document collects those licenses, alongside where each component lives, and ships inside the NuGet package so the notices travel with the binaries.

This package holds the data half of what used to be embedded directly in PeachDrawing.Text; the reader classes that consume it (`BidiClassTable`, `WordDictionary`, `HyphenationEngine`, and so on) still live in `PeachDrawing.Text`, under `PeachDrawing.Text.Internal.Text`, and are named where relevant below. See `PeachDrawing.Text`'s own [THIRD-PARTY-LICENSES.md](../PeachDrawing.Text/THIRD-PARTY-LICENSES.md) for the code-level third-party notices (PDFsharp, FreeType, HarfBuzz) that stayed with that package.

## Unicode Character Database (text-processing data tables)

- **Location:** [`Internal/Text/Resources/Bidi/`](Internal/Text/Resources/Bidi/) — `DerivedBidiClass.txt.br`, `BidiBrackets.txt.br`, `BidiMirroring.txt.br` (consumed by `PeachDrawing.Text.Internal.Text.Bidi.BidiClassTable`/`BidiBrackets`/`BidiMirroring`); [`Internal/Text/Resources/VerticalOrientation/`](Internal/Text/Resources/VerticalOrientation/) — `VerticalOrientation.txt.br` (`PeachDrawing.Text.Internal.Text.VerticalOrientationTable`); [`Internal/Text/Resources/Script/`](Internal/Text/Resources/Script/) — `Scripts.txt.br` (`PeachDrawing.Text.Internal.Text.ScriptTable`); [`Internal/Text/Resources/ArabicJoining/`](Internal/Text/Resources/ArabicJoining/) — `DerivedJoiningType.txt.br` (`PeachDrawing.Text.Internal.Text.ArabicShapingTable`); [`Internal/Text/Resources/Use/`](Internal/Text/Resources/Use/) — `IndicSyllabicCategory.txt.br`/`IndicPositionalCategory.txt.br` (`PeachDrawing.Text.Internal.Text.IndicSyllabicCategoryTable`/`IndicPositionalCategoryTable`). All Brotli-compressed. The line and text segmentation tables (UAX #14 and #29) are generated C# source instead, kept in `PeachDrawing.Text` itself (`Internal/Text/Segmentation/SegmentationData.g.cs`), since a WebAssembly host with no Brotli decoder still needs them.
- **Upstream source:** the Unicode Character Database (UCD), version 18.0.0, mirrored unmodified at [`assets/unicode/DerivedBidiClass.txt`](../../assets/unicode/DerivedBidiClass.txt), [`assets/unicode/BidiBrackets.txt`](../../assets/unicode/BidiBrackets.txt), [`assets/unicode/BidiMirroring.txt`](../../assets/unicode/BidiMirroring.txt), [`assets/unicode/VerticalOrientation.txt`](../../assets/unicode/VerticalOrientation.txt), [`assets/unicode/Scripts.txt`](../../assets/unicode/Scripts.txt), [`assets/unicode/DerivedJoiningType.txt`](../../assets/unicode/DerivedJoiningType.txt), [`assets/unicode/LineBreak.txt`](../../assets/unicode/LineBreak.txt), [`assets/unicode/EastAsianWidth.txt`](../../assets/unicode/EastAsianWidth.txt), [`assets/unicode/DerivedCoreProperties.txt`](../../assets/unicode/DerivedCoreProperties.txt), [`assets/unicode/GraphemeBreakProperty.txt`](../../assets/unicode/GraphemeBreakProperty.txt), [`assets/unicode/WordBreakProperty.txt`](../../assets/unicode/WordBreakProperty.txt), [`assets/unicode/SentenceBreakProperty.txt`](../../assets/unicode/SentenceBreakProperty.txt), [`assets/unicode/emoji-data.txt`](../../assets/unicode/emoji-data.txt), [`assets/unicode/emoji-variation-sequences.txt`](../../assets/unicode/emoji-variation-sequences.txt), [`assets/unicode/IndicSyllabicCategory.txt`](../../assets/unicode/IndicSyllabicCategory.txt), [`assets/unicode/IndicPositionalCategory.txt`](../../assets/unicode/IndicPositionalCategory.txt) — see [`assets/unicode/UnicodeCharacterDatabase.LICENSE.txt`](../../assets/unicode/UnicodeCharacterDatabase.LICENSE.txt)
- **Generation scripts:** [`assets/unicode/generate_bidi_tables.py`](../../assets/unicode/generate_bidi_tables.py), [`assets/unicode/generate_vertical_orientation_table.py`](../../assets/unicode/generate_vertical_orientation_table.py), [`assets/unicode/generate_script_table.py`](../../assets/unicode/generate_script_table.py), [`assets/unicode/generate_arabic_joining_table.py`](../../assets/unicode/generate_arabic_joining_table.py), [`assets/unicode/generate_segmentation_tables.py`](../../assets/unicode/generate_segmentation_tables.py) — each reparses its own UCD source file(s) above into the compact per-codepoint-range records the embedded resources actually ship, and each writes its output straight into this package's `Internal/Text/Resources/` rather than `PeachDrawing.Text`'s
- **Also present, not shipped:** `assets/unicode/BidiCharacterTest.txt`, the Unicode Consortium's own bidi conformance test suite, used only by `PeachDrawing.Text.Tests` to verify the Unicode Bidirectional Algorithm (UAX #9) implementation against real test vectors, as are `assets/unicode/LineBreakTest.txt`, `GraphemeBreakTest.txt`, `WordBreakTest.txt` and `SentenceBreakTest.txt` for UAX #14 and #29; `assets/unicode/ArabicShaping.txt`, kept alongside `DerivedJoiningType.txt` as the normative source that file is itself derived from, for provenance — neither is ever embedded in this package or its NuGet package
- **License:** [Unicode License v3](https://www.unicode.org/license.txt) ("Unicode® License Agreement — Data Files and Software")

Each UCD source file carries this notice in its own header, reproduced here rather than the full license text (see the license file linked above for that):

> © 2026 Unicode®, Inc. Unicode and the Unicode Logo are registered trademarks of Unicode, Inc. in the U.S. and other countries. For terms of use and license, see https://www.unicode.org/terms_of_use.html


## ICU word lists (Thai, Lao, Khmer and Burmese line breaking)

- **Location:** [`Internal/Text/Resources/Dictionaries/`](Internal/Text/Resources/Dictionaries/) — `thai.dict.br`, `lao.dict.br`, `khmer.dict.br` and `burmese.dict.br`, one embedded resource per script, read by `PeachDrawing.Text.Internal.Text.Segmentation.WordDictionary` (Brotli-compressed, like the other Unicode resources above; see that package's `Compression.BrotliDecompression` for the pluggable decoder on a host with no Brotli decoder of its own). Each is the word list of the break-iterator dictionary of the same script in ICU (`thaidict.txt`, `laodict.txt`, `khmerdict.txt`, `burmesedict.txt`), normalized (NFC, without zero width joiners), deduplicated and stored as a sorted, prefix-compressed list. They let the line breaking algorithm allow a break between the words of scripts that are written without spaces.
- **Upstream source:** [`icu4c/source/data/brkitr/dictionaries/`](https://github.com/unicode-org/icu/tree/release-78.3/icu4c/source/data/brkitr/dictionaries) of the ICU repository at the release tag `release-78.3`. The four files are not vendored: the generator downloads them from that tag and checks a SHA-256 for each.
- **Generation script:** [`assets/unicode/generate_dictionary_breaking.py`](../../assets/unicode/generate_dictionary_breaking.py)
- **Not used:** ICU's `cjdict.txt` (Chinese and Japanese break by the rules of the algorithm itself).

The ICU repository's `LICENSE` file, at that tag, puts the Thai and Khmer lists under the Unicode License v3 (they are ICU data: the header of each names Unicode, Inc., IBM and, for the Thai list, Apple), and gives the Lao and Burmese lists licenses of their own, reproduced below. Each license is reproduced in full for the list it covers.

### Thai and Khmer word lists: Unicode License v3

```
# Copyright (C) 2016 and later: Unicode, Inc. and others.
# License & terms of use: http://www.unicode.org/copyright.html
# Copyright (c) 2006-2015 International Business Machines Corporation,
# Apple Inc., and others. All Rights Reserved.       (thaidict.txt)

# Copyright (C) 2016 and later: Unicode, Inc. and others.
# License & terms of use: http://www.unicode.org/copyright.html
# Copyright (c) 2011-2015 International Business Machines Corporation
# and others. All Rights Reserved.                   (khmerdict.txt)
```

```
UNICODE LICENSE V3

COPYRIGHT AND PERMISSION NOTICE

Copyright © 2016-2025 Unicode, Inc.

NOTICE TO USER: Carefully read the following legal agreement. BY
DOWNLOADING, INSTALLING, COPYING OR OTHERWISE USING DATA FILES, AND/OR
SOFTWARE, YOU UNEQUIVOCALLY ACCEPT, AND AGREE TO BE BOUND BY, ALL OF THE
TERMS AND CONDITIONS OF THIS AGREEMENT. IF YOU DO NOT AGREE, DO NOT
DOWNLOAD, INSTALL, COPY, DISTRIBUTE OR USE THE DATA FILES OR SOFTWARE.

Permission is hereby granted, free of charge, to any person obtaining a
copy of data files and any associated documentation (the "Data Files") or
software and any associated documentation (the "Software") to deal in the
Data Files or Software without restriction, including without limitation
the rights to use, copy, modify, merge, publish, distribute, and/or sell
copies of the Data Files or Software, and to permit persons to whom the
Data Files or Software are furnished to do so, provided that either (a)
this copyright and permission notice appear with all copies of the Data
Files or Software, or (b) this copyright and permission notice appear in
associated Documentation.

THE DATA FILES AND SOFTWARE ARE PROVIDED "AS IS", WITHOUT WARRANTY OF ANY
KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT OF
THIRD PARTY RIGHTS.

IN NO EVENT SHALL THE COPYRIGHT HOLDER OR HOLDERS INCLUDED IN THIS NOTICE
BE LIABLE FOR ANY CLAIM, OR ANY SPECIAL INDIRECT OR CONSEQUENTIAL DAMAGES,
OR ANY DAMAGES WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS,
WHETHER IN AN ACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION,
ARISING OUT OF OR IN CONNECTION WITH THE USE OR PERFORMANCE OF THE DATA
FILES OR SOFTWARE.

Except as contained in this notice, the name of a copyright holder shall
not be used in advertising or otherwise to promote the sale, use or other
dealings in these Data Files or Software without prior written
authorization of the copyright holder.

SPDX-License-Identifier: Unicode-3.0
```

### Lao word list (laodict.txt): BSD-style license

The list is by Brian Eugene Wilson and Robert Martin Campbell, from <https://github.com/rober42539/lao-dictionary>, with special thanks to Erik Mundall, Arlyta Keosamone and Bualong Nyoukmai; ICU derived its file from the version of that dictionary of November 22, 2020, and modified the header. The notice, conditions and disclaimer of its license:

```
Copyright (C) 2013 Brian Eugene Wilson, Robert Martin Campbell.
All rights reserved.

Redistribution and use in source and binary forms, with or without modification,
are permitted provided that the following conditions are met:

	Redistributions of source code must retain the above copyright notice, this
	list of conditions and the following disclaimer. Redistributions in binary
	form must reproduce the above copyright notice, this list of conditions and
	the following disclaimer in the documentation and/or other materials
	provided with the distribution.

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

### Burmese word list (burmesedict.txt): BSD-style license

The list is by LeRoy Benjamin Sharon, with special thanks to Robert Martin Campbell, from the Myanmar Karen Word Lists project (<https://github.com/kanyawtech/myanmar-karen-word-lists>); ICU modified the header, deleted duplicate entries and some entries with unusual characters. The notice, conditions and disclaimer of its license:

```
Copyright (c) 2013, LeRoy Benjamin Sharon
All rights reserved.

Redistribution and use in source and binary forms, with or without modification,
are permitted provided that the following conditions are met:

  Redistributions of source code must retain the above copyright notice, this
  list of conditions and the following disclaimer.

  Redistributions in binary form must reproduce the above copyright notice, this
  list of conditions and the following disclaimer in the documentation and/or
  other materials provided with the distribution.

  Neither the name Myanmar Karen Word Lists, nor the names of its
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

## Hyphenation patterns (hyph-utf8 / CTAN)

- **Location:** [`Internal/Text/Resources/Patterns/`](Internal/Text/Resources/Patterns/) — one Brotli-compressed `hyph-<tag>.txt.br` file per language (73 files)
- **Upstream source:** the `hyph-utf8` package from CTAN, mirrored at [github.com/hyphenation/tex-hyphen](https://github.com/hyphenation/tex-hyphen)
- **Regeneration/provenance script:** [`tools/Update-HyphenationPatterns.ps1`](../../tools/Update-HyphenationPatterns.ps1), pinned to a specific upstream commit for reproducibility, and pointed at this package's `Internal/Text/Resources/Patterns/` rather than `PeachDrawing.Text`'s

Each language's original `hyph-<tag>.tex` source carries its own copyright and license notice (these patterns are contributed independently, by different authors, over several decades). `tools/Update-HyphenationPatterns.ps1` bundles **only permissively-licensed pattern sets** (MIT/LPPL/BSD-style/public-domain) and skips any whose resolved license is GPL/LGPL-family or unstated (see the script's `Test-PermissiveLicense` function) — consistent with this package's own BSD 3-Clause license. Each compressed pattern file also carries this same notice inline in its decompressed text header, alongside its title, copyright holder, and a source/retrieval-date/commit stamp.

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
