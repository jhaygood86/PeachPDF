# PeachBrackets.Scripting: webappapis, infrastructure, webmessaging and encoding WPT

Phase L. Four modules added (`html/webappapis`, `html/infrastructure`, `webmessaging`, and `encoding` without the `legacy-mb-*` directories), fetched with cache key `wpt-v6-`.
First runs and now: webappapis 632 -> 931 of 1534, html-infrastructure 229 -> 266 of 639, webmessaging 104 -> 130 of 207, encoding 4295 -> 10941 of 12559.

- **The runner crashed on a bad image.** `ScriptDocument.DecodeImage` caught only `InvalidOperationException`/`IOException`/`ArgumentException`, and the raster decoder throws `InvalidDataException`
  for bytes it does not recognize, so `createImageBitmap(blob)` of a non-image killed the whole process. It now also catches `InvalidDataException` and `NotSupportedException`.
- **`legacy-mb-*` is left out on purpose.** Those files hold about a million subtests (one per code point) and take minutes; the module lists the other encoding files and the `gb18030`/`gbk` directories.
- **TextDecoder decodes through stateful decoders** (`TextEncodings`, handles from `net.decoderNew`/`decoderRun`). UTF-16 is the standard's algorithm (lead byte, lead surrogate, one replacement for
  a truncated end), so streaming chunks that split a code unit or a pair work; the single-byte encodings are 256-entry tables built from the .NET code pages (registered with
  `CodePagesEncodingProvider`) with the bytes the standard leaves undefined listed by hand (they are what `fatal` rejects) and a few overrides where the .NET table differs (C1 controls of
  ISO-8859-x, ISO-8859-7/-8, macintosh, x-mac-cyrillic). The label table (`EncodingLabels`) had UTF-16 labels under UTF-8 and `x-user-defined` under windows-1252; fixed (`document.characterSet` still
  answers UTF-8 for UTF-16 labels). `XMLHttpRequest.responseText` uses the same decoders. The `single-byte-raw.py` handler was ported for the XHR variants of the single-byte tests.
- Not done: ISO-8859-10/-14/-16 (no .NET code page, no table), the CJK decoders' error recovery (a few hundred subtests in gb18030/gbk/iso-2022-jp), `encodeInto` into `SharedArrayBuffer`.
- `structuredClone` clones `Blob`, `File`, `ImageData`, BigInt wrapper objects, errors with their `cause` (and no own `message` when there was none), and ordinary objects whatever their prototype
  (own enumerable properties only); `toString` tag `[object Object]` is the test for "ordinary".
- `BroadcastChannel`, a forgiving `atob` and a checking `btoa`, `navigator.registerProtocolHandler`/`unregisterProtocolHandler`.
- Remaining big groups, not started: `document.open`/`write` (dynamic-markup-insertion, ~280), the query-encoding page tests (~190, need form/`<a>` charset handling), `HTMLAllCollection`/`document.all`, workers,
  unhandled-rejection events, `ImageBitmap` cloning, `SharedArrayBuffer`/resizable buffers.
