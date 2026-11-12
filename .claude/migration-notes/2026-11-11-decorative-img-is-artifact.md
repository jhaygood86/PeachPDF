# Decorative `<img>` is an artifact, not a Figure

Before: with tagged PDF on (`--pdfua`, PDF/A-a), `<img alt="">` and `<img>` with no alt and no accessible name were written as a `Figure` structure element with an empty `/Alt`, which fails PDF/UA-1 (ISO 14289-1 7.3-1).

Now: such images are marked-content artifacts and have no structure element. Images with a non-empty `alt` (or an accessible name) are unchanged.
