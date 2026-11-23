# WPT `text-transform-upperlower-016` expects the simple case mapping for Greek Extended

`css/css-text/text-transform/text-transform-upperlower-016.html` pairs `ᾀ` (U+1F80) with `ᾈ` (U+1F88) and expects `text-transform: uppercase` to give `ᾈ` for both, which is Unicode's *simple* uppercase mapping. We give `ἈΙ`, the *full* mapping from `SpecialCasing.txt`, so the test fails.

**Why it stays:** css-text-3 section 2.1.1 says the user agent "must use the full case mappings for Unicode characters, including any conditional casing rules", and the German sharp s (`ß` -> `SS`, `text-transform-upperlower-032`, which passes) needs the same rule. The two tests cannot both pass; the specification text is on the side of the full mapping, so this test is the one that is wrong (or out of date). Do not switch to the simple mapping to pick it up.

**Evidence:** `CaseMapperConformanceTests` checks every unconditional `SpecialCasing.txt` entry, `1F80; 1F80; 1F88; 1F08 0399` among them.
