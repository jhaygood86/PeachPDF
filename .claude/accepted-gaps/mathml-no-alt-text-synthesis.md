# The spoken form of a formula is plain, and there is no /ActualText

A tagged `Formula` structure element has an `/Alt`: the `<math>` element's `alttext`, its `aria-label`, or else a plain English reading from `MathSpeech`
(`x² + 1` is "x squared plus 1"). That reading covers fractions, roots, scripts, sums and limits, fences, tables, the usual operators and Greek letters; it is
not a full speech-rule system - no other language, no ClearSpeak or MathSpeak styles, no pausing or prosody hints. Nothing sets `/ActualText`.

This is arguably not a functional accessibility gap: the original MathML is attached to the element as a PDF 2.0 Associated File (`/AF`, see
[MathML Associated Files](../../docs/html-css-support.md#mathml-associated-files)), an exact and complete accessible representation that assistive technology can read
directly, which is better than any natural-language approximation in `/Alt`. An author who needs a precise reading gives `alttext`.
