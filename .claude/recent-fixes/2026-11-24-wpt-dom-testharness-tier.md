# The WPT runner's testharness tier (`run --testharness`, dom/)

The runner can now run `testharness.js` tests, not only reftests, and gates the DOM standard's `dom/` tests on a per-module floor of **passing
subtests** (`wpt/wpt-floors-dom.json`). Started at 1,391 of 5,558 subtests in `dom/nodes`; the first pass of DOM fixes it drove took the whole
`dom/` set to about 3,100 of 7,072.

**Load-bearing ideas:**
- The real `resources/testharness.js` runs, unmodified, in the Jint host. Only `testharnessreport.js` is replaced (served by the loader, never
  written to the checkout) with a script that logs one `WPT-RESULT {json}` line through `console.log`; the runner reads it from the host's
  `Console` callback. No new public API was needed for this.
- The harness timeout (a `setTimeout`) is a **virtual-clock** timer, so a test that never finishes ends `TIMEOUT` the moment nothing else is
  pending, in real milliseconds, not after ten seconds. The whole `dom/` set runs in about 12 s.
- `.any.js` / `.window.js` files are wrapped in the page the WPT server makes (`TestharnessEvaluator.Wrap`: harness, report, each
  `// META: script=`, the test, `done()`); `global=` without `window` is skipped. `.sub.` and worker variants are skipped.
- `resources/testdriver-vendor.js` does not exist in the checkout (only its `.headers`), so the loader answers it with an empty script;
  without it every test that loads testdriver.js timed out.
- The checkout fetch now also takes `dom/`, `html/resources/` and `interfaces/`, and the `.complete` marker records the list so an older cache
  is fetched again; the CI cache key got a `-v2-` so it does not restore a checkout without them.

**What running it found:** errors thrown as `Error("HierarchyRequestError: ...")` instead of a real `DOMException` (about 500 subtests), `Event()`
callable without `new`, `initEvent`/`createEvent` missing, `EventTarget`/`Text`/`DocumentFragment` not constructible, `NodeList`/`HTMLCollection`
not existing as types, `DOMTokenList` not following the ordered-set rules, `createElementNS` ignoring its arguments. All fixed with unit tests in
`DomStandardTests`. The rest is the node-model gap in [the accepted gap](../accepted-gaps/scripting-dom-has-no-nodes-without-a-box.md).

**Trap:** the `Js.Constructor` wrapper now throws when called without `new`, so a native caller must not call a DOM constructor as a function.
