# East Asian longhand counter styles now render their numerals

Before, `list-style-type` and `counter(n, style)` with `cjk-ideographic`, `japanese-formal`/`-informal`, `korean-hangul-formal`, `korean-hanja-formal`/`-informal`, `simp-chinese-formal`/`-informal` or `trad-chinese-formal`/`-informal` fell back to `decimal`. Now they write the numeral in that style (`十一`, `일십일`, `貳拾`...) and, as a list marker, end with the style's suffix (`、`, or `, ` for Korean) rather than `.`.
