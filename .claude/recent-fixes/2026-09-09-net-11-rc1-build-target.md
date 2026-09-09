# .NET 11 RC1 added as a third build target, opt-in per resolved SDK

`src/Directory.Build.props`'s shared `TargetFrameworks` gained `net11.0` alongside the
existing `net8.0;net10.0`, so `PeachPDF`, `PeachPDF.Tests`, and `PeachPDF.TestHarness` all
pick it up automatically (no per-project TFM override on any of the three) — but only when
the SDK actually resolved for the build is 11.0.100 or newer. The CLI
(`PeachPDF.Cli`/`PeachPDF.Cli.Tests`) and the Blazor WASM demo stay `net10.0`-only,
deliberately — they're explicit single-TFM overrides for reasons unrelated to which STS/LTS
targets the library supports (NativeAOT publish, one-framework-at-a-time WASM app), and
moving them wasn't part of this change.

## The load-bearing idea

A contributor with only the .NET 10 SDK installed must be able to build/test the repo with
zero extra setup — adding a third TFM can't come at the cost of hard-requiring a prerelease
SDK for everyone. That rules out simply bumping `global.json`'s pin to require 11.0: since
`global.json` governs SDK resolution for **every** `dotnet` invocation in the repo tree, a
hard 11.0 pin would break `net8.0`/`net10.0` builds too on a .NET-10-only machine
(`NETSDK1045`), not just a hypothetical net11.0-specific one.

The fix has two independent parts, and both matter:

1. **`global.json`** stays pinned at a `10.0.100` floor, but with
   `"rollForward": "latestMajor"` and `"allowPrerelease": true`. This resolves to the .NET 11
   RC SDK when it's installed (verified against the actual RC build,
   `11.0.100-rc.1.26425.128`) and gracefully falls back to the GA .NET 10 SDK when it isn't
   (verified by excluding the RC candidate via `allowPrerelease: false` and confirming
   resolution still lands on `10.0.401`) — auto-promotion with no manual `global.json` edits
   needed on either kind of machine.
2. **`src/Directory.Build.props`** only adds `net11.0` to `TargetFrameworks` when the
   resolved SDK actually supports it — otherwise a `net11.0`-listing multi-targeted project
   fails outright (`NETSDK1045`) the moment `global.json` falls back to the .NET 10 SDK,
   regardless of how permissive the roll-forward policy is. The check can't use
   `$(NETCoreSdkVersion)` directly: that property is confirmed **not reliably populated**
   at `Directory.Build.props`'s property-evaluation point — empty under the .NET 10 SDK,
   populated under the .NET 11 RC SDK, an internal import-ordering difference between SDK
   versions. `$(MSBuildExtensionsPath)` (the resolved SDK's own root folder) *is* reliably
   set that early on every SDK version tested, so the version is derived from its final path
   segment instead — after normalizing away a trailing separator, since
   `$(MSBuildExtensionsPath)` is observed to end with one under the .NET 10 SDK and without
   one under the .NET 11 RC SDK (another cross-version inconsistency caught only by directly
   printing the property under both).

CI (`test.yml`/`publish.yml`) needs no special-casing beyond installing the RC SDK
alongside the existing 8.0.x/10.0.x — `global.json`'s auto-promotion does the rest, so the
main test job picks up all three TFMs and the pack step ships all three in one nupkg.
`pages.yml`/`release-binaries.yml` don't build any net11.0 output (their commands are
explicitly pinned to net10.0 already) and needed no changes at all.

## What running it turned up (two false leads, both caught by testing before landing)

- **First attempt: pin `global.json` straight to the RC SDK.** Worked, but is exactly the
  "breaks .NET-10-only machines" problem described above — caught by literally trying a
  `net10.0`-only build against it (`NETSDK1045`), not by reasoning about it.
- **Second attempt (a wrong conclusion, corrected before landing): "`rollForward: latestMajor`
  can never select a prerelease SDK from a different major, no matter what."** Four separate
  experiments with different `version`/`allowPrerelease` combinations all appeared to confirm
  this, leading to a much heavier design (CI overwriting `global.json` per-job with `echo
  '{...}' > global.json` before its build steps, contributors told to hand-edit a local
  `global.json` to opt in). The conclusion was wrong: a stray `src/global.json` had been
  accidentally created during testing (a `cp global.json ...` run from the wrong cwd) and was
  shadowing the real repo-root one for every test after that point, so every experiment was
  silently reading the stray file's fixed content regardless of what the repo-root file said.
  Once found and deleted, `rollForward: "latestMajor"` was retested cleanly and confirmed to
  work exactly as hoped on the first and only real attempt — collapsing the whole design back
  down to the single-`global.json` version described above. Lesson: a `dotnet --version`/build
  result that doesn't match a just-made `global.json` edit is a signal to check for a shadowing
  file via the directory-walk the muxer itself does, not a signal to conclude something about
  SDK resolver semantics.

## What was deliberately not done

- No `NET11_0_OR_GREATER` conditional-compilation gate was added anywhere. The one existing
  TFM-conditional gate in the codebase (`RUri.MustBypassSystemUri`, see
  [2026-09-08-data-uri-bypass-is-per-target-framework-not-unconditional.md](2026-09-08-data-uri-bypass-is-per-target-framework-not-unconditional.md))
  is gated on `NET10_0_OR_GREATER`, which is a cumulative symbol — it stays defined when
  building net11.0, so the .NET-10-and-later `System.Uri` behavior already applies unchanged.
- The CLI, CLI.Tests, and Blazor demo were left on net10.0 rather than moved or dual-targeted
  — narrower change, and NativeAOT publish behavior on a still-RC SDK is less proven than
  ordinary library/test builds.

## Evidence

Full solution rebuild (`dotnet build PeachPDF.slnx -t:Rebuild`) verified clean (0 warnings,
0 errors) in both states: `global.json` with the RC SDK excluded (`net8.0;net10.0` output
only, no net11.0 anywhere) and with it present (`net8.0;net10.0;net11.0` output for
`PeachPDF`/`PeachPDF.Tests`/`PeachPDF.TestHarness`, nupkg containing all three `lib/` TFM
folders). Full suite green against net11.0 specifically
(`dotnet test --framework net11.0`: 10,419 passed, 0 failed, 9 skipped — OS-specific,
expected).

## Once .NET 11 GAs

`allowPrerelease: true` in `global.json` and the `dotnet-quality: 'preview'` steps in
`test.yml`/`publish.yml` exist only because 11.0 is pre-GA right now — once the GA SDK
ships, drop `allowPrerelease` (plain `rollForward: "latestMajor"` still auto-promotes to
whatever's newest and installed) and fold each workflow's separate "Setup .NET 11 (RC)"
step back into its main `dotnet-version` list.
