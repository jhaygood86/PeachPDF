# Script output goes through ILogger; user scripts (`--script`)

Phase 3 of the Prince-parity plan. `JintScriptOptions.Console` and `ScriptError` (callbacks) are gone: everything a script says and every failure goes to
`JintScriptOptions.Logger` (`Microsoft.Extensions.Logging.Abstractions`). `console.*` and the new `Log` object map to log levels; failures are `Error`
entries under `ScriptLogEvents.ScriptFailed`/`ScriptLoadFailed`; `Log.data(name, value)` is an `Information` entry under `ScriptLogEvents.Data` whose
state is a `ScriptDataEntry` (name/value pairs).

- The package reference is conditioned on `net10.0` in `PeachBrackets.Scripting.csproj`: `Microsoft.Extensions.Logging.Abstractions` is in the box on
  net11.0 (verified: the net11.0 build compiles without it), so only the net10.0 package pulls it in. PeachBrackets and PeachPDF gain no dependency.
- User scripts live on the host, not the engine: `JintScriptOptions.UserScripts` (run first, in order) and `RunAuthorScripts` (false = only those). This
  replaced the plan's `PdfGenerateConfig.UserScripts`, because a host option needs no engine or PeachPDF API change. The CLI's `--script FILE` builds
  such a host; alone it leaves the document's own scripts off, with `--enable-scripting` both run.
- `Log` is installed when `PrinceCompatibility` (default true) is on.
- The WPT `TestharnessEvaluator` now reads the harness result line from the logger instead of a console callback; tests use `CollectingLogger`.
- Checked with a NativeAOT publish: only Jint's own known IL2026/IL2104 warnings, and the native binary logs `Log.info`/`Log.data` to stderr.
