# PeachBrackets.Scripting: streams, fetch CORS and redirects, multipart FormData

Phase B of the WPT gap plan. fetch-api 1088 -> 1558 subtests, xhr 710 -> 715, fileapi 452 -> 458.

- **Streams** live in `Internal/WebStreamsScripts.cs`, a JS fragment spliced into the bundle at `WebPlatformScripts.StreamsMarker` so it
  shares the bundle's helpers. Default streams only (a `type: "bytes"` source is read through a default reader).
- **CORS and redirects are in the bundle's `fetch`**, not in the network: every hop goes to `net.send` with `followRedirects: false`, the
  loop decides what the hop means (opaque, opaqueredirect, preflight, method change on 301/302/303). The network sees plain requests.
- **Trap: a bundle function named `originOf` already existed** (the URL record's origin). A second declaration of the same name in the
  one function scope silently replaced it, so `URL.origin` returned `undefined` and every request looked same-origin; the CORS helper is
  `addressOrigin`. Symptom: `type` stayed `basic` and no preflight was ever sent.
- **Trap: closing a pipe from `reader.closed`** drops the last chunk, because the stream closes (and the `closed` handler is queued)
  before the pending `read()` that delivers the final chunk resumes. `pipeTo` ends on the `done` result of its own read instead and uses
  `closed` only for errors.
- A `Request`'s `clone()` went through `new Request(this)`, which marked the original body used; it now restores it and tees a stream body.
- Worker variants of `.any.js` tests (`global=window,worker`) fail by design: there are no workers (half of some files' subtests).
- Not done: credentials/cookie modes, the HTTP cache modes (`request-cache-*` ~100 subtests), `Request` upload streaming (`duplex`),
  BYOB readers and `ReadableByteStreamController`, the `streams/` WPT directory (not fetched yet).
