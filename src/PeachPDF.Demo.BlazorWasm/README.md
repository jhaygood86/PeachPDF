# PeachPDF in the browser

A Blazor WebAssembly app that renders an uploaded HTML file, MHTML archive, or ZIP of a site folder to
PDF **entirely client-side**. Nothing is uploaded anywhere, and the document being rendered can reach
neither the network nor the file system.

It is published to <https://peachpdf.net/demo/> by `.github/workflows/pages.yml`.

## Running it

```bash
dotnet run --project src/PeachPDF.Demo.BlazorWasm
```

No workload is required — a plain .NET 10 SDK is enough.

## How the three input kinds are handled

| Upload | Loader | Where the document comes from |
|---|---|---|
| `.html` / `.htm` | `DataUriNetworkLoader` | the uploaded file itself; only `data:` URIs resolve |
| `.mhtml` / `.mht` | `MimeKitNetworkLoader` | the archive's root part, resources matched by `Content-Location` |
| `.zip` | `ZipFileNetworkLoader` (local to this app) | `index.html`, `index.htm`, `default.html` or `default.htm` |

The kind is detected from the file's content where that is conclusive (a ZIP is a ZIP whatever it has been
renamed to) and from its extension otherwise, and can be overridden in the UI.

Every render sets `AllowLocalFileAccess = false`, and no HTTP loader is configured, so a reference to the
web or to a local file resolves to nothing.

## Things worth knowing

- **The tab freezes while rendering.** WebAssembly in the browser is single-threaded and PeachPDF's
  pipeline is synchronous once it starts. Making this genuinely responsive needs
  `WasmEnableThreads`, which requires COOP/COEP response headers that GitHub Pages cannot serve.
- **This is not a performance benchmark.** Blazor WebAssembly interprets IL rather than JIT-compiling it,
  so the same document renders far faster on any non-browser host. Enabling `RunAOTCompilation` would
  narrow the gap at a large cost in download size and build time, and it is not what this demo is for.
- **Fonts are WOFF 1.0, not WOFF2**, for a historical reason that no longer holds: `System.IO.Compression.BrotliStream`
  throws `PlatformNotSupportedException` in the browser (installing the `wasm-tools` workload to natively relink the
  runtime does not change it — measured; the limitation is managed-side), so this demo used to have no way to read
  WOFF2's Brotli-compressed tables at all. That gap is closed now (see the next bullet), but the shipped Liberation
  faces were not switched over — WOFF 1.0's deflate compression is close enough (about 2.3 MB for the twelve faces
  against WOFF2's 1.6 MB) that re-doing the OFL round-trip verification `convert_liberation_webfonts.py` requires
  wasn't worth it just for this demo's own UI text. A `@font-face` pointing at a WOFF2 file in an *uploaded* document
  works correctly, and is what actually exercises the fix (see the next bullet).
- **`hyphens: auto` and WOFF2 fonts both work now.** Program.cs registers a pure-managed Brotli decoder
  (`PeachDrawing.Text.Brotli`, gated on `OperatingSystem.IsBrowser()`) at startup, which every Brotli-compressed
  resource this library reads — the shared hyphenation/dictionary-line-breaking data and WOFF2 font tables alike —
  falls back to wherever the runtime's own `BrotliStream` would otherwise throw. `hyphens: auto` hyphenates
  normally here, Thai/Lao/Khmer/Burmese text wraps at dictionary word boundaries, and a WOFF2 `@font-face` in an
  uploaded document loads like any other font. See `PeachDrawing.Text.Brotli`'s own `PORTING-NOTES.md` for exactly
  what was ported and verified.
- **The footer names the build it is running.** A `GenerateDemoBuildInfo` target bakes in the library's
  `PackageVersion`, this commit, and whether the two agree — a release is tagged `v{PackageVersion}`, so a
  commit that is not that tag's commit is a prerelease of it, and the footer links the commit instead of
  the release. Both git calls tolerate failure, so the demo still builds from a source archive with no
  repository; with no commit to compare, the version alone is the honest answer. Note this makes
  `pages.yml` check out full history — a shallow clone has no tags, and every deploy would otherwise
  describe itself as a prerelease.
- **`Arial Narrow` renders at normal width.** Liberation Sans Narrow is not part of Liberation 2.x — it
  ships separately under a different licence — so no metrically compatible narrow face is bundled.
- **WebP images do not render.** PeachPDF's image decoder does not support the format.
- **ZIP entry names are read as UTF-8.** A legacy CP437-encoded archive with non-ASCII names will not
  match; `System.Text.Encoding.CodePages` is not available in this host.
- **`<meta charset>` is not honoured** when decoding an upload — a byte-order mark, or UTF-8, only.
- Font data is fetched once per page load and cached, but registration is per-render: PeachPDF registers
  fonts on a `PdfGenerator` instance, and the app builds a fresh one each time so one document's
  `@font-face` fonts cannot leak into the next.

## Where the fonts come from

The twelve Liberation faces live in the repository-root `assets/fonts/` directory, shared with
`PeachPDF.Tests` and `PeachPDF.TestHarness`. The project's `StageSharedFontAssets` target copies them into
`wwwroot/fonts/` at build time — those copies are build output and are gitignored. See
`assets/fonts/convert_liberation_webfonts.py` for the TrueType-to-WOFF conversion and the SIL OFL
conditions it has to satisfy to keep the Liberation name.
