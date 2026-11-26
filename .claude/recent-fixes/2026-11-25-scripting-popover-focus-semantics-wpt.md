# PeachBrackets.Scripting: html/semantics WPT, the Popover API, focus, and a CI hang

Phase M. `html/semantics` is fetched whole (cache key `wpt-v7-`) and run as nine modules: forms, scripting-1, interactive-elements, tabular-data, text-level-semantics,
grouping-content, sections, document-metadata, and the rest (selectors, disabled-elements, edits, links, the button element, popovers...). html-semantics-other went from
614 to 4091 of 5434 subtests; the others start low (forms 563/4726, scripting 798/2402) and were not worked on yet.

- **A hang in CI, found from a report that the WPT job never finished.** `RunPendingScriptsAsync` runs every connected script not yet in `_ran`. A script made by `innerHTML`
  (here `document.body.innerHTML += rows` from a script that sits in the body) is a new element, so it ran again, appended again, and so on, doubling the body each time (35 GB in
  the scripted css-flexbox test `overflow-auto-005.html`; the job was still running 25 minutes later). The fragment parsing algorithm marks the scripts it makes as
  already started; `ScriptNode.ParseInContext` now does (`CssBox.ScriptStarted`, public `ScriptNode.ScriptAlreadyStarted`, checked by `IsRunnable`).
  The unscripted reftest step never ran the page, so only the scripted step hung. Reproduced by `AppendingToBodyInnerHtml_FromAScriptAfterTheBodyEnded_Finishes`;
  `dotnet-stack report -p <pid>` on the hung test host showed `RunPendingScriptsAsync` -> `Execute` -> `set_InnerHtml`, which is what pointed at the re-run.
- **The popover API** is script (`PopoverScripts`) over the DOM plus one native, `setOpen`, that sets `CssBox.PopoverOpen` so `:popover-open` (and `:open` for details and dialog,
  `FormStateSelectors`) matches. Toggle events are coalesced per element as the standard says (a hide and show in one turn fire one `toggle` from the first old state).
  The invoker IDL (`popoverTargetElement`) reports the named popover for any element that has the attribute, not only for buttons that can invoke: the WPT expects that, and
  eligibility (enabled, not a submit button of a form) is checked at activation.
- **Focus** was a stub (`activeElement` was always the body). It is now the focusable-area rules and `blur`/`focusout`/`focus`/`focusin`; `focus()` on a popover's content only works
  while the popover is showing.
- **The runner runs each test on a thread of its own with a 128 MB stack.** A scripted page that recurses to the engine's depth limit (400 frames) overflowed a pool thread and
  killed the whole process (`popover-light-dismiss.html`); a timeout also used to leave a spinning test on the pool.
- Not done: top-layer rendering and the `[popover]:not(:popover-open) { display: none }` rule (so a hidden popover still lays out and prints), popover focus restoration and autofocus,
  `ElementInternals`, the constraint validation API (`validity`, `checkValidity`, about 1,500 subtests), text selection of inputs (`setSelectionRange`, about 500), `template` content and `template for`.
