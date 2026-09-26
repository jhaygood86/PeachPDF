# Line breaking has no dictionary for Lao and Burmese

`PeachDrawing.Text.Unicode.LineBreaker` breaks Thai and Khmer at the words of a dictionary, but a run of Lao or Burmese still gets rule
LB1's fallback (a nonspacing or spacing mark is `CM`, everything else `AL`), so it has no break opportunity inside it, where readers expect
one between words.

The word lists exist and were read: ICU's `laodict.txt` and `burmesedict.txt` at release-78.3, under BSD-style notices (Wilson and Campbell,
2013; Sharon, 2013), which the package would have to reproduce. They were left out for size: each list is an embedded resource, the package
carries one assembly per target framework, so a list costs its size three times, and Thai and Khmer alone take about 0.85 MB of the 1 MB the
feature was allowed (all four would have been about 1.4 MB; Lao is 79,001 bytes of resource and Burmese 113,347).

`assets/unicode/generate_dictionary_breaking.py` keeps both in `DEFERRED` with their hashes. The cluster rules the two scripts need were
written and tested on the branch `dictionary-line-breaking-all-four`, with the notices: no word starts before a dependent character (marks
and, for Lao, U+0EAF, U+0EB0, U+0EB2, U+0EB3, U+0EBD, U+0EC6) or ends on a leading one (Lao U+0EC0..U+0EC4, the Burmese virama U+1039), and a
Burmese consonant followed by an asat (U+103A) closes the syllable before it, unless the asat is followed by a virama (a kinzi, which
begins one). Tracked in [#1457](https://github.com/jhaygood86/PeachPDF/issues/1457): pick how the package pays for them (a smaller
encoding, a bigger budget, or one copy outside the per-framework assemblies), then finish the change. Text-side normalization
(the lists are NFC, text is not) matters most for Burmese, whose marks are often typed in a non-canonical order.
