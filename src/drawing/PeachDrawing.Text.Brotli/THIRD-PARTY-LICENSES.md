# Third-Party Licenses (PeachDrawing.Text.Brotli)

PeachDrawing.Text.Brotli is BSD 3-Clause-licensed (see [LICENSE](../../LICENSE)), but the decoder it wraps is
ported from third-party work carrying its own license terms.

## google/brotli (ported C# decoder)

- **Location:** [`Internal/`](Internal/) — the Brotli bitstream decoder (bit reading, Huffman table
  construction, context modeling, the static dictionary and its word transforms, the decoding state machine),
  ported from Google's own C# port of the Brotli reference decoder. `ManagedBrotliDecompressor.cs`, the public
  wrapper outside this directory, is original PeachPDF code and carries no Brotli notice.
- **Upstream source:** [google/brotli](https://github.com/google/brotli), `csharp/org/brotli/dec/`, commit
  `11017d7812b0502005603cf0f3cce0054e885d72` (`master`), retrieved 2026-09-27
- **License file:** [`Internal/LICENSE`](Internal/LICENSE), byte-identical to upstream's own `LICENSE`
- **Changes from the original:** recorded in [`PORTING-NOTES.md`](PORTING-NOTES.md) — a namespace rename
  (`Org.Brotli.Dec` → `PeachDrawing.Text.Brotli.Internal`), narrowing `BrotliInputStream` from `public` to
  `internal`, a `#nullable disable` per file, and two bug fixes in `BrotliInputStream.Read` (returning the .NET
  end-of-stream value instead of the Java one it was transliterated with, and not discarding already-copied bytes
  when reporting end-of-stream) found by this project's own tests and review pass.
- **License:** MIT

```
Copyright (c) 2009, 2010, 2013-2016 by the Brotli Authors.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.  IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
```
