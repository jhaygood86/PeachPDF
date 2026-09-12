# No /Alt or /ActualText spoken-math synthesis for a tagged Formula structure element

`StructureTagBuilder.AttachMathMlSource` attaches a `<math>` element's original MathML markup as a PDF
2.0 Associated File on its `Formula` structure element (see
[MathML Associated Files](../../docs/html-css-support.md#mathml-associated-files)), but does not set the
structure element's `/Alt` (alternate description) or `/ActualText` (a textual substitute for the marked
content) entries to any kind of generated spoken-math description (e.g. "x squared plus one" for
`x^2 + 1`). Generating one would require a real math-to-speech engine - out of scope for this project.

This is arguably not a functional accessibility gap rather than a deliberate omission: the `/AF`-attached
exact MathML source is itself a complete, spec-conformant accessible representation per PDF/UA-2 (assistive
technology reads the attached MathML directly, rather than relying on a lossy natural-language
approximation baked into `/Alt`). Recorded here mainly so a future reader auditing PDF/UA-2 conformance
claims for this feature knows the omission was considered, not missed.
