# ScriptApi: hosts add JavaScript objects without referencing Jint

Phase 1b of the Prince-parity plan. `PeachBrackets.ScriptHosting.ScriptApi` (named global; properties, methods, events built from delegates),
`ScriptValue` (a struct: undefined/null/bool/number/string/array/object/function/node) and `ScriptCallback` are public, Jint-free, in PeachBrackets.
`HtmlDocumentOptions.ScriptApis` / `PdfGenerateConfig.ScriptApis` carry them; `ScriptDocument.Apis` is how the host sees them.

**Load-bearing ideas:**
- **The engine, not the host, raises the API's lifecycle events** (`ScriptsStarting`, `ScriptsCompleted`, `LayoutCompleted`, `LayoutFinished`, `Closed`),
  so an API works with any `IScriptHost`. An API that subscribes to a layout event makes the container keep the script document live
  (`ScriptApi.ObservesLayout`); one that does not is released after the scripts, as before.
- `Closed` is raised once per scripted document whether or not it was kept (`_apisStarted` in `HtmlContainerInt`), and `IScriptHost.DocumentClosed`
  is now called for released documents too, so the Jint runtime can unsubscribe from the API's `EventRaised`.
- **The probe copy must not see the APIs**: `CloneWithoutScripts` clears `ScriptApis`. Without that, the probe's own `RunScriptsAsync` would raise
  `ScriptsStarting`/`Closed` on the real objects.
- `ScriptsStarting` exists because an API cannot otherwise reach the `ScriptDocument` before the scripts that call it run.
- `Log` is now built as a `ScriptApi` inside the Scripting package (`BuildLogApi`), so there is one path and it is tested by the API tests. `Prince` is
  still hand-built: it needs the post-layout list and an EventTarget with the `complete` handler, and gains nothing from the seam yet.
- A method that throws becomes an `Error` the script can catch (`HostCall`); the script engine's own exception types are not wrapped.

**Not done:** conversion limits are minimal (objects 8 deep, own enumerable string keys; functions cross as callbacks, no `this`); values are not live
(an array or object is copied at the boundary).
