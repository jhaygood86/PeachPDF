# `ccmp`/`locl` are applied exactly once — never zero times, never twice

`ccmp` (Glyph Composition/Decomposition) and `locl` (Localized Forms) are **default-on** for every script
in the OpenType feature registry — not stylistic opt-ins like `liga`/`dlig`. Three places in
`GsubShaper.Shape` can apply them, and exactly one must win for any given run:

1. the Arabic joining pre-stage (gated on `features.JoiningForms` being non-empty),
2. the USE pre-stage (gated on `features.UseCategories` being non-empty),
3. the main per-feature pass, via `GetActiveLookupIndices`' `defaultTags`.

(3) is therefore gated on **neither** (1) nor (2) having run. Both halves of that gate are load-bearing.

**Zero times is a real, shipped bug.** Before 2026-09-10, `ccmp`/`locl` were reachable only from (1) and
(2), so all other text got none. That is not an edge case: a modern color-emoji font can keep *every*
sequence ligature in `ccmp` and define no `liga`/`rlig` whatsoever — Noto Color Emoji's entire `GSUB`
FeatureList is a single `ccmp` record — so `🇺🇸` rendered as the letters "US" and `🏴󠁧󠁢󠁳󠁣󠁴󠁿` as a bare black
flag.

**Twice is not idempotent.** A Type 2 (Multiple Substitution) `ccmp` decomposition will happily decompose
its own output again — a font that splits a precomposed dotted letter into base + mark can be made to
split the result a second time. Do not "simplify" the gate by always adding the tags and letting the
pre-stage run too.

The trap that hid all of this: **one case worked, by accident.** ZWJ (U+200D) has
`Joining_Type=Join_Causing`, so an emoji ZWJ sequence produces a non-empty `JoiningForms` array and trips
the *Arabic* pre-stage, picking up `ccmp` as a side effect. `🏳️‍🌈` therefore composed correctly while
every non-ZWJ sequence did not — which made emoji look supported and kept the gap alive. If you are
reasoning about whether a GSUB feature is reaching a run, check a sequence with **no** ZWJ in it.

Regression guard: `DefaultIgnorableShapingTests.CcmpLigature_AppliesWithoutAnyLigatureFeatureRequested`
against `assets/fonts/CcmpLigatureTest.ttf`, whose only GSUB feature is `ccmp`; and
`ArabicJoiningCharacterizationTests.NoJoiningFormsRequested_StillAppliesCcmp_ButOnlyOnce`, whose
same-glyph-count-either-way assertion is what actually proves the gate holds.

See [`.claude/recent-fixes/2026-09-10-ccmp-is-default-on-and-default-ignorables-are-never-drawn.md`](../recent-fixes/2026-09-10-ccmp-is-default-on-and-default-ignorables-are-never-drawn.md).
