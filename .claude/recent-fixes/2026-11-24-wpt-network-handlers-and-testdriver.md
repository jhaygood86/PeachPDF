# WPT: ported network handlers, testdriver actions, referrer policy, floor margins

**What.** `Server/NetworkHandlers.cs` ports about 45 of the `.py` handlers `xhr/` and `fetch/api/` use (status with its own reason phrase, content/echo, redirect with a counting stash, preflight, the
`access-control-*` fixtures, conditional/cache, stash put/take/clean). The server gained a real stash (`ServerStash`, scoped by handler directory, `Take` removes), `ServerRequest.Server`,
`ServerResponse.StatusText`, and an `Origin` header on any request that is not a same-origin GET/HEAD. `ScriptNetworkRequest` now carries `Referrer` and `ReferrerPolicy` (public, set by the
bundle's `fetch`/XHR); `WptServer.Send` makes the `Referer` per hop from the policy and lets a `Referrer-Policy` response header change it for the next hop.
Evidence: xhr 285 -> 709, fetch-api 647 -> 1088, fileapi 411 -> 452 passing subtests.

**Testdriver.** `TestdriverVendor` fills `testdriver-vendor.js`: click, send_keys, action_sequence and bless dispatch the events in the page, so interaction tests run instead of erroring (html-dom 28.6k -> 34.4k).
**Memory.** The runner raises the script memory limit to 1 GiB (the 256 MiB default stopped `Range-set` and friends: dom-ranges 38k -> 40k).
**Floors.** `wpt-floors-dom.json` has a `margins` map; `--update-floors` writes `measured - margin` and still never lowers a floor, so the load-sensitive modules need no hand-lowering.

**Traps.** A handler that sleeps or streams in Python answers whole and at once here (the clock is virtual); `cache.py`-style tests need an HTTP cache model and stay failing. The `request-cache-*` tests use
`fetch/api/request/resources/cache.py`, not ported. `echo-headers.py` prints Python's repr of the raw header list and is not ported.
**Not done (phase B).** CORS enforcement (175 "Should have rejected"), streams, FormData bodies, credentials/redirect-mode rules, `Origin` on every cross-origin request in a real browser's cases.
