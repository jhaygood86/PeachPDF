# Guard the net11.0 opt-in against a non-version `$(MSBuildExtensionsPath)` (VS2026)

`src/Directory.Build.props`'s net11.0 opt-in (see
[2026-09-09-net-11-rc1-build-target.md](2026-09-09-net-11-rc1-build-target.md)) derives
`ResolvedNETCoreSdkVersion` from the final path segment of `$(MSBuildExtensionsPath)`, then feeds
it straight into `[MSBuild]::VersionGreaterThanOrEquals(...)`. Issue #1130 reported that under
VS2026 Insiders that property resolves to VS's own MSBuild toolset path
(`...\MSBuild\`) rather than a dotnet-SDK version folder, so the derived segment came back as the
literal string `"MSBuild"`. `VersionGreaterThanOrEquals` doesn't fail soft on an unparseable
string — it's a hard MSBuild evaluation error (`MSB4184`), which meant VS2026 couldn't even load
the project, not just mis-evaluate the TFM list.

## The fix

Added `ResolvedNETCoreSdkVersionLooksVersioned`, a
`System.Text.RegularExpressions.Regex.IsMatch` check (`^\d+(\.\d+){2,3}`) gating the existing
condition. MSBuild's `AND` short-circuits, so `VersionGreaterThanOrEquals` is only ever called once
the string has already been confirmed to look like a version — an unrecognized value just leaves
`TargetFrameworks` at the `net8.0;net10.0` default instead of crashing evaluation. Deliberately not
special-cased to the literal `"MSBuild"` string: the actual defect is "an arbitrary string reached
a function that assumes a parseable version," and some other IDE/MSBuild host could plausibly
produce a different non-version value here in the future.

## Verified

A standalone probe `.proj` (isolated from the real SDK-style project, since overriding
`$(MSBuildExtensionsPath)` directly on `PeachPDF.csproj` via `-p:` also breaks unrelated
SDK-internal targets that read the same property) reproduced the exact reported `MSB4184` error
against the pre-fix logic when `MSBuildExtensionsPath` was set to a VS2026-shaped path, and
confirmed the post-fix logic: falls back cleanly (no crash) for the VS2026 path and for an empty
string, while still adding `net11.0` for both the real installed RC SDK path
(`11.0.100-rc.1.26425.128`) and a simulated GA path (`11.0.100`). Full solution rebuild
(`dotnet build PeachPDF.slnx -t:Rebuild`) stayed clean (0 warnings/0 errors, all three TFMs still
produced on this machine).
