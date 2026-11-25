# PeachBrackets.Scripting: the form control model and the constraint validation API

Phase N. `html-forms` went from 563 to 2695 of 4787 subtests (floor 2650); no other module moved down.

- **There was no form model at all.** `input.value` reflected the `value` attribute, `form`, `elements`, `select.options` and `selectedIndex` did not exist, so every validation test failed on the
  first property. `FormScripts` (evaluated at startup with the popover script, over the per-tag prototypes `HTMLInputElement.prototype` and so on) adds: the value of each input mode (value, default,
  default/on, filename) with a dirty flag and the sanitization algorithm of each type, `checked` with radio groups (group = same name and form owner), `valueAsNumber`/`valueAsDate`/`stepUp`/`stepDown`
  over the date, time, week and month microsyntaxes, `textarea.value`, `select` and `option` selectedness, the form owner (`form` attribute or ancestor) and `form.elements`.
- **ValidityState is computed on demand from the element, never cached**, and its flags apply to a disabled or read-only control too: only `willValidate`, `checkValidity` and `reportValidity` consult
  "barred from constraint validation". `valueMissing` is the exception because it needs a mutable control (the WPT data says `expectedImmutable: false`). I first returned all-false for a barred
  control and every `validity.*` test failed with "when control is disabled" in the message.
- **A pattern is validated alone before it is wrapped:** `new RegExp(pattern, "v")` must compile by itself, then `^(?:pattern)$` is used. `a)(b` compiles inside the wrapper (it closes the group and opens
  another), which is the escape the standard forbids.
- **Events the user agent fires are trusted** (`invalid`, `select`): `native.fire` creates the event in C# with `IsTrusted` set, because an event made in script is never trusted. `select()` and
  `setSelectionRange` fire `select` only when the selection changed.
- **A type change must go through the IDL setter and the attribute methods:** `input.type = x` is a native accessor that writes the attribute without calling `setAttribute`, so the value conversion
  (value to default mode stores the old value as the attribute, and so on) is hooked on both the `type` setter and the wrapped `setAttribute`/`removeAttribute`.
- Not done: `:valid`, `:invalid`, `:in-range`, `:out-of-range` and `:user-*` (the selector engine cannot ask script for state; `FormStateSelectors` would need the same kind of flag `:popover-open` got), user
  edits (`tooLong`, `tooShort`, `badInput` never become true), the `files`/`FileList` model, `color` input conversion to the sRGB hex of a CSS color (`color.window.js`, ~100), week and month
  type-change tests that need `input type=week` support in more places, `form.requestSubmit` and the submit algorithm (`form-double-submit`, `urlencoded2`, ~230), `labels`.
