# The engine hands out a `Typeface`; the PDF writer's font wrapper layer is gone

Third step of the engine extraction (after [the face-style types](2026-09-25-engine-owns-its-face-style-types.md)).
`XGlyphTypeface` moved into the engine as `Typeface` (`Fonts/Typeface.cs`) and the rest of PDFsharp's font wrapper layer
was deleted. `XFont` is what is left of it: a `Typeface` plus an em size, PDF font options and a skew.

**What a `Typeface` is:** one resolved font face, independent of any size. It owns the `OpenTypeFontface` and, new here,
the `OpenTypeDescriptor` (metrics, glyph mapping, shaping, outlines in design units), created on first use.

**Why the descriptor moved onto it.** There used to be a second cache for descriptors (`FontDescriptorCache` globally, plus
a `FontResolver.InstanceFontDescriptorsByKey` per instance for custom families), keyed by the *same* string as the glyph
typeface cache, purely to hand every `XFont` of one typeface the same descriptor. Typefaces are already cached by that key
(per resolver instance for custom families, globally for system families), so a typeface that owns its descriptor gets the
identical sharing with one cache instead of three, and the cross-`PdfGenerator` isolation for a custom family registered
with different bytes falls out of the typeface cache's existing split. A test pins both properties
(`Typeface_IsCachedPerFamilyAndStyle_AndOwnsExactlyOneDescriptor`, `Typeface_OfACustomFamily_IsNotSharedWithAnotherResolverInstance`).

**Deleted** (no users, or only users that were themselves being deleted): `XFontFamily`, `XFontMetrics`, `XTypeface`,
`XPrivateFontCollection`, `FontFamilyCache`, `FontFamilyInternal`, `FontDescriptorCache`, `XFontWeight(s)`, `XFontStretch`,
`XGraphicsPath.AddString` (a not-implemented stub), and `PdfSharpCore.Internal.Lock` (its font-lock forwarders lost their last
callers; `PSSR`'s resource manager, its only other user, now has a private lock of its own). `FontFamilyAdapter` only ever
needed a family *name*.

**Trap avoided:** `FontData` was already the name of a property on `OpenTypeFontTable`, which is why the file-bytes type is
`FontFileData`; likewise the descriptor is reached as `Typeface.Descriptor`, not by a new type named after the property.

**Deliberately not done:** `XFont` and the `XGraphics` string API (`DrawString`, `MeasureString`, `XStringFormat`) still exist.
Replacing them with a size-free `Typeface` plus a glyph-run draw primitive touches the PDF renderer's hot path and is the next
step.
