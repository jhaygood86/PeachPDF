# Every input that sets a variable-font axis is in the font cache keys

A font is a function of more than its family and size once a variable face is involved: the weight, the width **as a percentage**, the
oblique angle and the encoded `font-variation-settings`/`font-optical-sizing` all choose the location the font is read at. So each cache
between the box and the PDF has to carry them, and a cache that keys by less serves one box's location to another:

- `FontsHandler._fontsCache`, `_codepointFontsCache` and `_systemFallbackFontsCache` are keyed by `(style, weight, stretch, obliqueSkewSinus,
  variations)` next to the size and `LayoutUnitsPerPoint` (see [the size and scale invariant](fonts-a-cached-font-is-identified-by-its-size-and-its-scale.md)).
  `stretch` is the percentage (`double`, 100 is normal), **not** the OpenType class: 87.5% and 88% are different locations, the class
  cannot tell them apart.
- The engine's typeface key (`FontResolvingOptions.ComputeTypefaceKey`) carries the class when the percentage is a class percentage and
  `w<percent>` otherwise, so the classic keys (`tk:f/n/400/5`) did not change.
- The per-codepoint typeface key (`cp/<face name>/...`) is keyed by the face the request resolved to, and **two registrations of one file
  share a face name** (the internal name) while declaring different ranges, so the resolved face's declared ranges are part of that key
  too. Without them a second `@font-face` rule for the same file was served the first rule's typeface, at the first rule's axis range.
- A `Typeface` for one location is one object (`WithVariation` memoizes on the normalized location), so anything keyed by a typeface's
  identity or `VariationKey` (the PDF font table, `FontAdapter.FaceKey`) follows for free.

A new input to the axes (a new query field that becomes an axis setting) has to be added to all of the above at once. The symptom of a
miss is a document whose output depends on which box asked first.
