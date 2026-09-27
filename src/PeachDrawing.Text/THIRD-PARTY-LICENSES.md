# Third-Party Licenses (PeachDrawing.Text)

PeachDrawing.Text is BSD 3-Clause-licensed (see [LICENSE](../../LICENSE)), but the source it is built from includes third-party work, each part carrying its own license terms. This document collects those licenses, alongside where each component lives, and ships inside the NuGet package so the notices travel with the binaries.

The Unicode Character Database tables, the ICU word lists and the `hyph-utf8` hyphenation patterns PeachDrawing.Text reads at runtime moved into their own package, `PeachDrawing.Text.Data` (a dependency of this one, so it is always installed alongside it) — see [that package's own THIRD-PARTY-LICENSES.md](../PeachDrawing.Text.Data/THIRD-PARTY-LICENSES.md) for their notices.

## PDFsharp (derived font-file readers)

- **Location:** [`Internal/Fonts/`](Internal/Fonts/) and [`Internal/Fonts/OpenType/`](Internal/Fonts/OpenType/): the OpenType/TrueType table readers, the font resolver contracts and the subsetting writer derive from the PDFsharp project's font code. The 22 files that derive from it carry the PDFsharp header (copyright, authors, project URL and the full MIT permission notice) unchanged at the top of the file.
- **License file:** [`Internal/Fonts/OpenType/LICENSE.md`](Internal/Fonts/OpenType/LICENSE.md)
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

## FreeType (ported TrueType bytecode interpreter and CFF loader)

Portions of this software are copyright © 1996-2026 The FreeType Project (https://freetype.org). All rights reserved.

This software is based in part on the work of the FreeType Team.

- **Location:** [`Internal/Hinting/FreeType/`](Internal/Hinting/FreeType/): the TrueType bytecode interpreter and glyph loader used for grid-fitting (hinting) outlines, ported to C# from FreeType 2.14.3 (`ttinterp`, `ttobjs`, `ttgload`, `ttpload`, `ttgxvar`, `ftcalc` and the part of `fttrigon` those need). Nothing derived from FreeType lives anywhere else in the package. Each ported file begins with the header of the FreeType file it derives from, unchanged, followed by a line saying it was ported to C# and modified.
- **License file:** [`Internal/Hinting/FreeType/FTL.TXT`](Internal/Hinting/FreeType/FTL.TXT), the FreeType Project License exactly as distributed by FreeType (it ships in this package unaltered)
- **Changes from the original:** recorded, per file, in [`Internal/Hinting/FreeType/PORTING-NOTES.md`](Internal/Hinting/FreeType/PORTING-NOTES.md), which also names the FreeType release ported (VER-2-14-3)
- **License:** the FreeType Project License (FTL), a permissive BSD-style license with a credit clause. FreeType is dual-licensed under the FTL or the GNU GPL version 2; PeachDrawing.Text uses it under the FTL only. The FTL does not restrict the license of the rest of this package, and does not permit using the names of the FreeType authors or contributors to promote a product without their written permission.

An application that redistributes this package in binary form has to say, in its documentation, that its software is based in part on the work of the FreeType Team. The credit line above is the text FreeType suggests for it.

### Adobe's CFF engine (part of FreeType)

- **Location:** the `Ps*.cs` files of [`Internal/Hinting/FreeType/`](Internal/Hinting/FreeType/), the C# port of the `psaux` module of FreeType 2.14.3 (`psarrst`, `psblues`, `psfixed`, `psfont`, `psft`, `pshints`, `psintrp`, `psstack`, `psglue`, `pserror`, `psread`), which Adobe Systems Incorporated contributed to FreeType. It grid-fits CFF (PostScript) outlines. The files that load a CFF font around it (`Cff*.cs`) derive from FreeType's own `cff` module.
- **Copyright:** Copyright 2006-2014 Adobe Systems Incorporated (the years differ by file; each ported file begins with the header of the C file it derives from, unchanged).
- **License:** the FreeType Project License (`FTL.TXT`, above), with an additional patent licence grant. The header says that the work is made available under the FreeType Project License, and that each contributor grants everyone who exercises the permissions of that licence a perpetual, worldwide, non-exclusive, no-charge, royalty-free, irrevocable patent licence to make, have made, use, offer to sell, sell, import and otherwise transfer the work, for the patent claims the contributor can license that its contribution necessarily infringes; and that the patent licences a licensee holds terminate as of the date on which the licensee starts patent litigation against any entity alleging that the work or a contribution in it infringes a patent. Using, modifying or distributing the work means accepting the FreeType Project License and that grant.
- **Changes from the original:** recorded, per file, in [`Internal/Hinting/FreeType/PORTING-NOTES.md`](Internal/Hinting/FreeType/PORTING-NOTES.md). Only the parts for CFF and CFF2 (variable CFF) fonts were ported; the parts for Type 1 fonts were not.

## HarfBuzz (ported Arabic/Syriac joining state machine)

- **Location:** [`src/PeachDrawing.Text/Internal/Text/Shaping/Arabic/ArabicJoiningStateTable.cs`](Internal/Text/Shaping/Arabic/ArabicJoiningStateTable.cs), [`src/PeachDrawing.Text/Internal/Text/Shaping/Arabic/ArabicJoiningShaper.cs`](Internal/Text/Shaping/Arabic/ArabicJoiningShaper.cs) — a line-by-line C# port of the cursive-joining state machine (`arabic_state_table`/`arabic_joining`), not merely inspired by it
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

Everything else in `src/PeachDrawing.Text/Internal/Text/Shaping/Arabic/` (`ArabicJoiningForm.cs`) is original PeachPDF code (BSD 3-Clause, the project's own license) written to consume the ported state machine — not itself derived from HarfBuzz, so it carries no HarfBuzz notice.


## HarfBuzz (ported GPOS cursive attachment formula)

- **Location:** [`src/PeachDrawing.Text/Internal/Text/GposPositioner.cs`](Internal/Text/GposPositioner.cs) — the `TryApplyCursivePair` method's main-direction (X) correction only (the surrounding dispatch/iteration and the cross-direction Y correction are original PeachPDF code)
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

- **Location:** [`src/PeachDrawing.Text/Internal/Text/Shaping/Use/UseSyllableScanner.cs`](Internal/Text/Shaping/Use/UseSyllableScanner.cs) (a hand-written scanner implementing the same grammar as the ported `.rl` source below, standing in for HarfBuzz's own Ragel-generated state machine since this repo has no Ragel toolchain) and [`src/PeachDrawing.Text/Internal/Text/Shaping/Use/UseReorderer.cs`](Internal/Text/Shaping/Use/UseReorderer.cs) (`ReorderSyllable`, a line-by-line port of `reorder_syllable_use`)
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
above for the full text (identical here). Everything else in `src/PeachDrawing.Text/Internal/Text/Shaping/Use/`
(`UseCategory.cs`, `UseSyllableType.cs`, `UseSyllable.cs`) is original PeachPDF code (BSD 3-Clause, the
project's own license) written to consume the ported algorithm — not itself derived from HarfBuzz, so
it carries no HarfBuzz notice.


