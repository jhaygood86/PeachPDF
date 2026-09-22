# `outline-style: hidden` is now rejected as invalid

**Before:** PeachPDF accepted `hidden` as an `outline-style` value and suppressed the outline. In a
rule where an earlier declaration had already set one — `outline: 2px solid red; outline-style:
hidden` — the `hidden` declaration won and the ring silently stopped painting.

**Now:** the keyword is rejected. [css-ui-4 §3.3](https://www.w3.org/TR/css-ui-4/#outline-style)
defines `outline-style` as `auto | <'border-style'>` **excluding** `hidden` — unlike every
`border-*-style`, where the keyword is both valid and meaningful. An invalid declaration is dropped
([CSS Syntax 3](https://www.w3.org/TR/css-syntax-3/)), so the earlier `outline: 2px solid red` now
survives and keeps painting, as it does in a browser. The `outline` shorthand rejects the keyword on
the same grounds, so `outline: 2px hidden red` is dropped whole.

**What an author may need to change:** a rule that used `outline-style: hidden` to turn off an
outline set earlier now leaves that outline visible. Use `outline-style: none` (or `outline: none`),
which is valid and does suppress it.

**Scope:** only `outline-style`. `border-style: hidden` and `column-rule-style: hidden` remain valid
and are unaffected — see
[the border-width note](2026-09-22-hidden-border-width-occupies-no-space.md) for the separate change
to how much space those reserve.
