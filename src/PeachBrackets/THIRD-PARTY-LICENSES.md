# Third-Party Licenses (PeachBrackets)

PeachBrackets is BSD 3-Clause-licensed (see [LICENSE](../../LICENSE)). The source it is built from includes the following
third-party work, which carries its own terms. The notices ship inside the NuGet package.

## HtmlRenderer (the layout engine's lineage)

The box model, layout and paint pipeline descend from [HtmlRenderer](https://github.com/ArthurHub/HTML-Renderer) by José Manuel
Menéndez Poo and Arthur Teplitzki, released under the BSD 3-Clause license reproduced in [LICENSE](../../LICENSE).

## ExCSS (CSS parser, adapted in-tree)

- **Location:** [`CSS/`](CSS/)
- **License file:** [`CSS/license.txt`](CSS/license.txt)
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

## PeachDrawing.Text

The font and text engine this package depends on ports parts of FreeType; its notices, including the FreeType Project License, are in
the `PeachDrawing.Text` package and in `FTL.TXT` here.
