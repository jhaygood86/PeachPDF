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
