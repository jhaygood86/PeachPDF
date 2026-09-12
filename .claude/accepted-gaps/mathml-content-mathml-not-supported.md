# Content MathML is not supported at all

PeachPDF's MathML support (`MathTreeBuilder`/`MathLayoutEngine`/`MathRenderer`, see
[docs/architecture.md](../../docs/architecture.md#mathml-rendering)) implements Presentation MathML only.
Content MathML (`<apply>`, `<ci>`, `<cn>`, and the rest of MathML 3's semantic/functional markup
vocabulary) has no parser/layout support whatsoever — an unrecognized element degrades to a plain row of
its children via `MathTreeBuilder.BuildNode`'s default case, which produces meaningless output for
Content MathML's own element set (they carry no visual layout information of their own to fall back on).

This was a deliberate v1 scope decision, not an oversight: Content MathML is rarely authored directly —
most real-world documents that carry it also carry an equivalent Presentation MathML rendering, typically
via a `<semantics>` wrapper (which PeachPDF does render, using only the first, presentation-markup
child — see [Supported MathML Features](../../docs/supported-mathml-features.md#actions-and-semantic-annotations)).
Implementing Content MathML would require an entirely separate semantic-to-visual layout layer with no
shared code with the presentation engine.
