# Blazor WASM demo: runtime-async must be off for the demo and every library it ships

**Symptom:** the demo built, loaded and accepted an upload, but Generate PDF showed `InvalidProgramException:` with an
empty message (nothing in the console). The browser's Mono interpreter cannot execute runtime-async methods.

**Cause:** `src/Directory.Build.props` compiles every net11.0 target with `runtime-async=on`. The note in
[2026-11-10-dotnet-11-sdk-required.md](2026-11-10-dotnet-11-sdk-required.md) says Blazor is unaffected, which was true
while the demo was net10.0; moving it to net11.0 (#2405) silently opted it in.

**Fix:** `PeachRuntimeAsync=false` skips the `Features` line in `Directory.Build.props` and gives that build its own
`bin\no-runtime-async\` / `obj\no-runtime-async\` (only `OutputPath` and `IntermediateOutputPath`; `BaseIntermediateOutputPath`
must not move, restore reads it and does not see a ProjectReference's AdditionalProperties - tried, NETSDK1004). The demo
turns the flag off for itself and passes the property to each library reference. The libraries stay net11.0.

**Traps found by running it:**
- `AdditionalProperties` on a ProjectReference reach only that reference's build. The SDK resolves its *transitive*
  references again with the consumer's properties, so the first attempt shipped runtime-async copies of
  PeachDrawing.*/PeachBrackets next to the good ones and still threw. The demo therefore lists PeachPDF's whole project
  closure and sets `DisableTransitiveProjectReferences`. A new library under PeachPDF has to be added to that list.
- Prove it by hashing: compare each `bin\Debug\net11.0\<name>.dll` in the demo output with
  `<project>\bin\no-runtime-async\Debug\net11.0\<name>.dll`; all must match.
- `dotnet run` (what `.claude/launch.json` does) rebuilds, so a flag passed only to an earlier `dotnet build` is lost.

**Evidence:** Debug and Release builds 0 warnings, `dotnet publish` (as `pages.yml`) succeeds, and in the browser an uploaded
HTML file renders to `t.pdf` with a Download button. The normal runtime-async PeachPDF net11.0 build is unchanged.
