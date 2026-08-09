# `list-style-type`: East Asian longhand counter styles not supported

The 10 CSS Counter Styles Level 3 §7.1 "Longhand East Asian Counter Styles" - `cjk-ideographic`,
`japanese-formal`/`japanese-informal`, `korean-hangul-formal`/`korean-hanja-formal`/`korean-hanja-informal`,
`simp-chinese-formal`/`simp-chinese-informal`, `trad-chinese-formal`/`trad-chinese-informal` - fall back
to plain `decimal` numbering (the standard "unknown/unsupported style" fallback, CSS Counter Styles
Level 3 §2) rather than rendering their real longhand numerals.

Each is its own large sign-value/myriad-group numbering system (units, tens, hundreds, thousands,
myriad-group multipliers) with a distinct character vocabulary per language and per formal/informal
register - simplified vs. traditional Chinese, formal vs. informal Japanese, hangul vs. hanja Korean.
Correctly sourcing and verifying each character table is a separate, larger effort than the rest of
`list-style-type`'s value coverage (the Indic/SE Asian digit-substitution scripts, `cjk-decimal`,
`cjk-earthly-branch`/`cjk-heavenly-stem`, `ethiopic-numeric`, Armenian variants,
`disclosure-open`/`disclosure-closed`, and a literal `<string>` marker, all of which *are* supported -
see [docs/html-css-support.md](../../docs/html-css-support.md#lists)), so it was deliberately left out
of that pass. Tracked as [issue #684](https://github.com/jhaygood86/PeachPDF/issues/684).
