# .NET 11 SDK is now required; net11.0 is always built; runtime-async on for net11.0

`global.json` pins SDK 11.0.100 (`rollForward: latestFeature`, no prereleases). `src/Directory.Build.props` sets
`TargetFrameworks` to `net10.0;net11.0` unconditionally, as do the five platform csprojs (Win2D, WinUI, Android, Apple,
CoreGraphics). The `$(MSBuildExtensionsPath)` SDK-sniffing (and its VS2026 "MSBuild" evaluation trap) is gone; a
`RequireDotNet11Sdk` target errors clearly for IDE hosts that ignore `global.json`.

net11.0 targets (`$(TargetFramework.StartsWith('net11.0'))`) compile with `<Features>$(Features);runtime-async=on</Features>`.
It must stay TFM-conditioned: the .NET 10 runtime cannot run runtime-async methods. Single-TFM net10.0 projects (Cli/AOT,
Blazor, benchmarks) are unaffected.

CI: the composite `setup-dotnet` action installs 11.0.x (GA, no `dotnet-quality`) plus 10.0.x for the net10.0 test legs;
`include-net11` and the macOS net11.0 exclusion are removed.

Evidence: `dotnet build` of PeachPDF (both TFMs) 0 warnings; PeachPDF.Tests net11.0 with runtime-async: 1798 passed, 0 failed.

## C# 15 for every target

`LangVersion` is `latest` repo-wide (C# 15 under the 11 SDK), so net10.0 gets language-only C# 15 features too. Used so far:
collection-expression arguments (`[with(StringComparer.OrdinalIgnoreCase), "a", "b"]`) replacing `new(comparer) { ... }`
initializers on ~30 static sets, and `[with(capacity: n), ...]` in `Canvas.cs`. `with(...)` cannot be used for a `Dictionary`
target (the `Add` check needs a single-argument overload). The netstandard2.0 source generator keeps its own `LangVersion`
and was deliberately left alone. Unions were not adopted: they need runtime support types that net10.0 lacks.
