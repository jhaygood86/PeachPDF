# PeachBrackets.Scripting: what a run was left waiting for

Phase I of the WPT gap plan (the timeout diagnostic).

- When the scripts stop (`ScriptRuntime.RunAsync` after `PlayTimersAsync`) the runtime logs `ScriptLogEvents.PendingWork`: the timers left (count, next due,
  whether the virtual-time or callback limit stopped them, their function names), requests and images not answered, and the last eight events delivered
  (`type@target`, kept in `_recentEvents` by `DispatchEvent`). Information level when something is left, debug when not. It is public API (`PublicApi.txt`).
- The WPT runner (`TestharnessEvaluator`) keeps the last such entry and appends it in brackets to every timed-out subtest and to a harness timeout, in the
  `PEACHBRACKETS_WPT_SUBTESTS` log and the failure reason. Run the whole testharness suite with that variable set and group the `Test timed out [...]` lines.
- **What it found** (168 timed-out subtests over the 13 modules): every one says "nothing scheduled", so none is a delivery-order or virtual-time-limit bug; the pages wait
  for things the engine never produces: scroll and `scrollend`, CSS animation and transition events (`transitionend`, `animationend`), `<link rel=preload>`, blob
  URLs in tags and navigations, `moveBefore`, sandboxed-iframe messaging. The `last events` tail names what did arrive.
- **Fixed from it**: `click()` was a no-op. It now sends a bubbling, cancelable `MouseEvent`, and a click on a checkbox (or inside one, when it bubbles) toggles
  it before the listeners run, puts it back if one cancels, and fires `input` and `change` when not (`DispatchEvent`, `ActivationTarget`).
- The 1 GiB per-script memory limit and the per-module floor margins the plan asked for already exist (`TestharnessEvaluator`, `wpt-floors-dom.json` `margins`).
