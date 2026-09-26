# Line breaking has no dictionary for Thai, Lao, Khmer and Burmese

`PeachDrawing.Text.Unicode.LineBreaker` resolves the Complex_Context (`SA`) class the way UAX #14's LB1 does when no external
criteria are available: a nonspacing or spacing mark becomes `CM`, everything else `AL`. A run of Thai, Lao, Khmer or Burmese
therefore has no break opportunity inside it, where readers expect one between words.

A correct answer needs a dictionary and a segmentation algorithm over it, a data and licensing decision that does not belong
with the algorithm and its conformance suites. Tracked in
[#1400](https://github.com/jhaygood86/PeachPDF/issues/1400); the hook is `LineBreakAlgorithm.BuildUnits`, where `SA` is resolved.
