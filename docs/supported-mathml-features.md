# Supported MathML Features

PeachPDF renders inline `<math>` elements — MathML embedded directly in HTML markup — as real vector
PDF content: glyphs are drawn through the same text pipeline as ordinary HTML text, fraction bars and
radical rules are native PDF path/fill operators, and stretchy operators (parentheses, brackets, radical
signs, ...) are built from the font's own [OpenType `MATH` table](https://learn.microsoft.com/en-us/typography/opentype/spec/math)
glyph data. Nothing is rasterized to a bitmap.

The layout algorithm targets [MathML Core](https://w3c.github.io/mathml-core/) — the W3C/browser-vendor
specification that gives MathML 3's presentation markup a fully concrete box model and layout algorithm
(the parent [MathML 3](https://www.w3.org/TR/MathML3/) specification leaves layout details
implementation-defined). This page documents exactly what is and is not supported; where a feature is
only partially supported, the specific gaps are noted.

A `<math>` element needs a font with a real `MATH` table to render structurally correct fraction bars,
radicals, script sizing, and stretchy operators — see [Fonts](../README.md#fonts) for how PeachPDF
resolves fonts, and [Fallback when no `MATH` table is present](#fallback-when-no-math-table-is-present)
below for what happens without one.

---

## Root Element

| Element | MDN Reference | Notes |
|---------|---------------|-------|
| `math` | [math](https://developer.mozilla.org/en-US/docs/Web/MathML/Reference/Element/math) | The `display` attribute (`"block"`/`"inline"`, MathML Core §2.1) sets the formula's initial `displaystyle`. This only affects MathML Core's own sizing rules (e.g. whether a fraction's numerator/denominator shrink) — it does not change the CSS box-level `display` of the element itself; a `<math>` element always participates in ordinary HTML inline flow, the same way an inline `<svg>` does. Genuine CSS properties — `color`, `font-family`, `font-size`, `background` — cascade into `<math>` content normally, from the surrounding document. An explicit CSS `width`/`height` (including a percentage, resolved against the containing block) resizes the element's own box independently of its content — per MathML Core, `<math>` is not a replaced element the way `<img>`/`<svg>` are, so the formula's internal typesetting is never rescaled to fit; content can overflow a box smaller than its natural size, or leave extra space in a larger one, exactly like an ordinary block whose content doesn't fill its author-specified size |

## Token Elements

| Element | MDN Reference | Notes |
|---------|---------------|-------|
| `mi` | [mi](https://developer.mozilla.org/en-US/docs/Web/MathML/Reference/Element/mi) | Identifier text, drawn via the ordinary text pipeline |
| `mn` | [mn](https://developer.mozilla.org/en-US/docs/Web/MathML/Reference/Element/mn) | Numeric literal text |
| `mo` | [mo](https://developer.mozilla.org/en-US/docs/Web/MathML/Reference/Element/mo) | Operator/fence/separator text. `form` (`prefix`/`infix`/`postfix`), `stretchy`, `fence`, `separator`, `lspace`/`rspace`, `minsize`/`maxsize` are read when the author sets them explicitly. Any of `form`/`stretchy`/`symmetric`/`largeop`/`movablelimits`/`lspace`/`rspace` the author leaves unset falls back to MathML Core's own [operator dictionary](#operator-dictionary) (Appendix B) default for that character, rather than a flat approximation — e.g. a bare `<mo>(</mo>` stretches by default with no explicit `stretchy`/`fence` attribute needed. `accent` is parsed but not consulted for a default (see [Script and Limit Schemata](#script-and-limit-schemata)'s `munder`/`mover`/`munderover` row) |
| `mtext` | [mtext](https://developer.mozilla.org/en-US/docs/Web/MathML/Reference/Element/mtext) | Plain text, not italicized by default (per spec) |
| `ms` | [ms](https://developer.mozilla.org/en-US/docs/Web/MathML/Reference/Element/ms) | String literal text |
| `mspace` | [mspace](https://developer.mozilla.org/en-US/docs/Web/MathML/Reference/Element/mspace) | `width`/`height`/`depth` — full MathML length grammar, including the named space keywords (`thinmathspace` … `veryverythickmathspace`, and their `negative...` variants) |
| `mathvariant` | [mathvariant](https://developer.mozilla.org/en-US/docs/Web/MathML/Reference/Global_attributes/mathvariant) | A single-character `mi` is automatically italicized by substituting its Unicode "math italic" codepoint (MathML Core §4.2's `text-transform: math-auto`, Appendix C.1) — e.g. a lone variable `x` renders as the slanted 𝑥, while a multi-character identifier like `exp` does not. `mathvariant="normal"` cancels this. No other `mathvariant` value has any effect, which matches MathML Core itself: it defines no bold/italic/double-struck/etc. mapping at all, and instead expects authors to use the corresponding Unicode Mathematical Alphanumeric Symbols characters directly |

## General Layout Schemata

| Element | MDN Reference | Notes |
|---------|---------------|-------|
| `mrow` | [mrow](https://developer.mozilla.org/en-US/docs/Web/MathML/Reference/Element/mrow) | Horizontal concatenation of its children. Inter-element spacing around an `mo` with no explicit `lspace`/`rspace` comes from the full [operator dictionary](#operator-dictionary) (MathML Core Appendix B) |
| `mfrac` | [mfrac](https://developer.mozilla.org/en-US/docs/Web/MathML/Reference/Element/mfrac) | Numerator/denominator stacked around a real, measured fraction rule, using the font's own `MATH` table constants (`FractionRuleThickness`, `FractionNumeratorGapMin`/`DenominatorGapMin`, `AxisHeight`). `linethickness` override is supported. `bevelled="true"` is parsed but renders as an ordinary (non-slanted) fraction — an accepted gap |
| `msqrt` / `mroot` | [msqrt](https://developer.mozilla.org/en-US/docs/Web/MathML/Reference/Element/msqrt) / [mroot](https://developer.mozilla.org/en-US/docs/Web/MathML/Reference/Element/mroot) | A real, measured vinculum (radical rule) over the radicand, plus the radical sign itself drawn from the font's `MATH` table `MathVariants` vertical constructions (stretched to match the radicand's height) when the font provides one. The sign's reserved horizontal width is an approximation of the current font size rather than the chosen variant glyph's own true advance width (`MathVariants` exposes only the vertical growth-direction measurement, not width) — a minor accepted gap that can produce a little extra or a little tight spacing next to a tall/short radicand. `mroot`'s index is positioned and sized per MathML Core (two script levels smaller, kerned before/after) |
| `mstyle` | [mstyle](https://developer.mozilla.org/en-US/docs/Web/MathML/Reference/Element/mstyle) | `displaystyle`, `scriptlevel`, `mathsize`, `mathcolor` are honored for the subtree; `mathbackground` and the deprecated MathML 2 presentation attributes it also accepted are not rendered |
| `mspace` | (see Token Elements above) | |
| `mphantom` | [mphantom](https://developer.mozilla.org/en-US/docs/Web/MathML/Reference/Element/mphantom) | Reserves its content's exact size; draws nothing |
| `mpadded` | [mpadded](https://developer.mozilla.org/en-US/docs/Web/MathML/Reference/Element/mpadded) | `width`/`height`/`depth`/`lspace`/`voffset` (each a plain CSS `<length-percentage>` — a percentage value falls back to that attribute's own default, per spec) override the box's reported size and reposition its content within it, without rescaling the content itself. `height`/`depth` both default to the content's own natural *ascent* when absent (MathML Core's own default, not a bug) |
| `mfenced` | [mfenced](https://developer.mozilla.org/en-US/docs/Web/MathML/Reference/Element/mfenced) *(deprecated in MathML Core; still supported here)* | Desugared at parse time into an `mrow` of synthesized fence/separator `mo` elements (`open`/`close`/`separators` attributes, cyclically repeating the separator list) — the same shape hand-authoring the equivalent `mrow` would produce |
| `menclose` | [menclose](https://developer.mozilla.org/en-US/docs/Web/MathML/Reference/Element/menclose) *(not in MathML Core; supported here for MathML 3 compatibility)* | Parsed (including the `notation` attribute), but renders as a plain pass-through of its content — no enclosure mark is drawn. An accepted gap |
| `merror` | [merror](https://developer.mozilla.org/en-US/docs/Web/MathML/Reference/Element/merror) | Renders its content plainly, like an `mrow` — MathML Core drops the "highlight as a syntax error" presentational suggestion |

## Script and Limit Schemata

| Element | MDN Reference | Notes |
|---------|---------------|-------|
| `msub` / `msup` / `msubsup` | [msub](https://developer.mozilla.org/en-US/docs/Web/MathML/Reference/Element/msub) / [msup](https://developer.mozilla.org/en-US/docs/Web/MathML/Reference/Element/msup) / [msubsup](https://developer.mozilla.org/en-US/docs/Web/MathML/Reference/Element/msubsup) | Shift-up/shift-down and script-size scaling (`ScriptPercentScaleDown`) come from the font's `MATH` table constants, with a minimum-clearance check between a simultaneous sub- and superscript (`SubSuperscriptGapMin`). Per-glyph corner kerning (`MathKernInfo`) is not applied — an accepted gap; scripts are still correctly shifted and sized, just without the extra fine-grained horizontal kerning a `MathKernInfo`-aware engine would add |
| `munder` / `mover` / `munderover` | [munder](https://developer.mozilla.org/en-US/docs/Web/MathML/Reference/Element/munder) / [mover](https://developer.mozilla.org/en-US/docs/Web/MathML/Reference/Element/mover) / [munderover](https://developer.mozilla.org/en-US/docs/Web/MathML/Reference/Element/munderover) | An explicit `accent`/`accentunder="true"` renders that script at full size (not shrunk), matching an accent placed directly over/under the base; otherwise it's treated as a moving limit and shrunk one script level, like a sub/superscript. This is MathML Core's own default (§3.4.2: an absent or invalid `accent`/`accentunder` attribute is simply `false`) — it does not consult the base's core operator, unlike full MathML 3 |
| `mmultiscripts` | [mmultiscripts](https://developer.mozilla.org/en-US/docs/Web/MathML/Reference/Element/mmultiscripts) | Pre- and post-script pairs (`<mprescripts/>`-separated), including the `<none/>` placeholder for an omitted half of a pair |

## Tables and Matrices

| Element | MDN Reference | Notes |
|---------|---------------|-------|
| `mtable` | [mtable](https://developer.mozilla.org/en-US/docs/Web/MathML/Reference/Element/mtable) | A real grid: each column sized to its widest cell, each row to its tallest, centered on the table's own vertical center. Per-column/per-row `columnalign`/`rowalign` are parsed but every cell is currently centered regardless of the requested alignment — an accepted gap |
| `mtr` / `mlabeledtr` | [mtr](https://developer.mozilla.org/en-US/docs/Web/MathML/Reference/Element/mtr) / [mlabeledtr](https://developer.mozilla.org/en-US/docs/Web/MathML/Reference/Element/mlabeledtr) | `mlabeledtr`'s label cell is parsed and carried on the row but not yet drawn (equation numbering is a rare feature for a v1) — an accepted gap |
| `mtd` | [mtd](https://developer.mozilla.org/en-US/docs/Web/MathML/Reference/Element/mtd) | Cell content lays out at `displaystyle="false"` unless overridden, per MathML Core's own table default |

## Operator Dictionary

An `<mo>`'s `form`/`stretchy`/`symmetric`/`largeop`/`movablelimits`/`lspace`/`rspace` — whichever the
author doesn't set explicitly — resolve from MathML Core's own [operator
dictionary](https://w3c.github.io/mathml-core/#operator-dictionary) (Appendix B): the character's `form`
classifies it into one of 13 categories, each with its own spacing and property defaults (e.g. `+`/`-`
get a slightly tighter space than a relational operator like `=`; parentheses and other fence characters
default to `stretchy`/`symmetric` with zero space). An operator matching no category (most identifiers
used as `mo`, or an unrecognized character) falls back to a flat default matching MathML's own
`thickmathspace`.

## Stretchy Operators

A single-character `mo` that's stretchy — `stretchy="true"`/`fence="true"` explicit, or (per the
operator dictionary above) a character the dictionary marks stretchy by default, like `(`/`)`/`{`/`}` —
inside an `mrow` stretches vertically to match the tallest non-stretchy sibling in that row, per MathML Core §5.3.2's
"shape a stretchy glyph" algorithm: the font's `MATH` table `MathVariants` pre-sized glyph variants (e.g.
a font's several progressively taller parenthesis glyphs) are tried first, and if none is tall enough, a
taller shape is assembled from the font's `GlyphAssembly` parts (a top cap, a repeatable extender, and a
bottom cap, for a typical fence) — the same construction real browsers use, so arbitrarily tall content
(deeply nested fractions inside parentheses, a large matrix, ...) stretches the fence to match rather than
clamping at the font's largest pre-sized step.

## Fallback When No `MATH` Table Is Present

A font with no `MATH` table (the common case — only dedicated math fonts like STIX Two Math or Latin
Modern Math carry one) still renders a formula: every constant the layout algorithm needs falls back to
a fixed ratio of the current font size (e.g. axis height ≈ 0.25em, fraction rule thickness ≈ 0.04em) —
MathML Core's own documented fallback strategy for this case. Structural correctness (fractions,
radicals, scripts, tables) is preserved; stretchy operators fall back to their plain, unstretched glyph,
since `MathVariants` data doesn't exist to stretch from.

## Elementary Math

`mstack`/`mlongdiv`/`msgroup`/`msrow`/`mscarries`/`mscarry`/`msline` (MathML 3 §3.5, dropped entirely by
MathML Core) are **not supported** — arithmetic-stack/long-division layout is a large, separate algorithm
with no browser reference implementation to check against, and is rare outside elementary-education
material. An unrecognized element degrades to a plain row of its children.

## Actions and Semantic Annotations

| Element | MDN Reference | Notes |
|---------|---------------|-------|
| `maction` | [maction](https://developer.mozilla.org/en-US/docs/Web/MathML/Reference/Element/maction) | Renders only its selected child (`selection`, 1-based, default 1) or its first child — MathML Core itself reduces `maction` to this minimal behavior; PDF has no notion of the toggle/tooltip/statusline interactivity MathML 3 otherwise defines for it |
| `semantics` | [semantics](https://developer.mozilla.org/en-US/docs/Web/MathML/Reference/Element/semantics) | Renders only its first child (the presentation-markup branch) |
| `annotation` / `annotation-xml` | [annotation](https://developer.mozilla.org/en-US/docs/Web/MathML/Reference/Element/annotation) | Never laid out or painted — semantic/source-format metadata only |

## Content MathML

Content MathML (`<apply>`, `<ci>`, `<cn>`, and the rest of the semantic/functional markup vocabulary) is
**not supported at all**. It's rarely authored directly (most tools that produce it also produce an
equivalent presentation-MathML rendering, typically via a `<semantics>` wrapper — see
[Actions and Semantic Annotations](#actions-and-semantic-annotations) above), and implementing it would
require an entirely separate semantic-to-visual layout layer.

---

## Accepted Gaps

Each gap noted above with "an accepted gap" is tracked in this repository's internal engineering notes
(`.claude/accepted-gaps/`) alongside the reasoning for leaving it out of v1 and, where it's a genuine
spec deviation, a tracked issue for closing it later. In summary:

- `mfrac[bevelled]` and `menclose`'s notation marks are parsed but not drawn — neither is part of MathML
  Core at all (Chromium/WebKit don't render them either; Firefox's support for them is a pre-Core legacy
  holdover).
- No `MathKernInfo` (sub/superscript corner) kerning — confirmed optional even under MathML Core itself.
- `mtable`/`mtr` cell/row alignment attributes (`columnalign`/`rowalign`) are parsed but every cell
  centers — not part of MathML Core (Chromium/WebKit don't support it either).
- `mlabeledtr`'s label is parsed but not drawn — not part of MathML Core (Chromium/WebKit don't support
  it either).
- Elementary math (`mstack`/`mlongdiv` and its children) and Content MathML are not implemented at all —
  both dropped entirely by MathML Core.
