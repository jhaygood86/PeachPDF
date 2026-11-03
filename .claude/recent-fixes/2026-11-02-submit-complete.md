# Form submission, completed

What the previous note left out of submission, and the parser rule behind the last item.

- **Implicit submission.** `CreateImplicitSubmission(field)`: the first enabled submit button of the form is the default button and is used as the submitter;
  with none, the form submits only if it has at most one input that blocks implicit submission (text-like, date/time and number types). A textarea never
  submits (Enter is a line break there; the hosts only look for the form for one-line fields). Both hosts call it for Enter, and Android also for the IME's
  action key, which used to just move the focus on.
- **Validation.** `GetValidationProblems(trigger)` runs the constraints a browser runs before a submit and returns one problem per control (first match in
  this order: required, minlength, maxlength, type syntax, pattern, min/max/step). A radio group is checked once at its first member. `pattern` is wrapped
  as `^(?:pattern)$` with a one-second timeout, and an invalid or timed-out expression passes the value (a browser ignores a bad pattern). Hosts validate
  after building the submission: invalid -> focus the first control, raise `FormInvalid`, no `FormSubmitting`. The CSS `:valid`/`:invalid` pseudo-classes
  are still not matched.
- **Image buttons** send `name.x`/`name.y` (CSS px from the button's top left, 1px = 0.75pt) and no `name=value`; a keyboard or UI Automation activation sends 0, 0.
- **Reset.** `ResetFormAsync` drops every override the document recorded for the form's controls (`_formChanges`, `_chosenFiles`, text-area scroll, the
  edit session) and styles the document again. Trap: a drop-down keeps its state on its *options* (`SetFlag(option, "selected")`), so their overrides have to
  go too - the first test left the select on "b". Hosts: a click or tap on `IsResetButton` queues the reset like the other restyles.
- **Formatting reconstruction.** When a start tag ends an element and formatting elements are open on top of it, `ParseHtmlTag` closes them with it and opens
  clones inside the new element (`FormattingOpenInsideAnEndedElement`). It covers every implied end (`li`, `dt`/`dd`, cells, rows, `option`, and `p` closed by a
  block); it is not the adoption agency algorithm, so a *misnested end tag* (`<b><p>x</b>y`) is still handled the old way.
- **Font weight (issue 2137).** The text-node `FontWeight` read 900 inside `<b>` because `ActualNumericWeight` steps a relative keyword again on every
  descendant; the builder now reads the element's own weight. The deviation itself is tracked in the issue.
- **Not done:** `:valid`/`:invalid` pseudo-classes, localisation of the validation messages (English only), `datetime-local`/`week` step checking, a
  constraint-validation API on individual controls (`setCustomValidity`, `reportValidity`), and the adoption agency algorithm.
