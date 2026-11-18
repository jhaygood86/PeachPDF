# Third-Party Licenses (PeachDrawing.PDF)

PeachDrawing.PDF is BSD 3-Clause-licensed (see [LICENSE](../../LICENSE)). The source it is built from includes the following third-party work, which carries its own terms. The notice ships inside the NuGet package so it travels with the binaries.

## PDFsharp (the PDF writing engine, adapted in-tree)

- **Location:** [`Engine/`](Engine/)
- **License file:** [`Engine/LICENSE.md`](Engine/LICENSE.md)
- **License:** MIT

The full text is in the license file above:

> Copyright (c) 2001-2024 empira Software GmbH, Troisdorf (Cologne Area), Germany
> Copyright (c) 2017-2026 Justin Haygood

## sRGB IEC61966-2.1 color profile (`Engine/Resources/ColorProfiles/sRGB2014.icc`)

Embedded as the output intent PDF/A and PDF/X documents require; its license text is `Engine/Resources/ColorProfiles/sRGB2014.LICENSE.txt` beside it.

## Adobe CMap resources (`Import/CMaps/PdfCMaps.bin`)

- **Source:** https://github.com/adobe-type-tools/cmap-resources
- **License:** BSD-3-Clause

The predefined CMaps of ISO 32000 (Japan1, GB1, CNS1 and Korea1) and the CID-to-Unicode tables derived from their Unicode CMaps, compiled into one embedded resource by the generator test in `PeachDrawing.PDF.Tests`:

> Copyright 1990-2023 Adobe. All rights reserved.
>
> Redistribution and use in source and binary forms, with or without
> modification, are permitted provided that the following conditions are
> met:
>
> Redistributions of source code must retain the above copyright notice,
> this list of conditions and the following disclaimer.
>
> Redistributions in binary form must reproduce the above copyright
> notice, this list of conditions and the following disclaimer in the
> documentation and/or other materials provided with the distribution.
>
> Neither the name of Adobe nor the names of its contributors may be
> used to endorse or promote products derived from this software without
> specific prior written permission.
>
> THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
> "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
> LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
> A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT
> HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
> SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
> LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
> DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
> THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
> (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
> OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
