# Fonts are registered and matched through the public `FontSet`

`PdfSharpAdapter` no longer holds a `FontResolver`. It holds a `FontSet` (`PeachDrawing.Text`), registers fonts with
`AddData`, matches with `TypefaceFamily.TryMatch` (a character to cover) or `FontSet.MatchOrFallback` (none), asks
`TryFindCoveringFamily` for the last-resort search, and gets the platform's generic families from
`FontSet.ResolveGeneric`. `XFont` is now built from a `TypefaceMatch` and an em size; its resolver-taking constructors, and
the dev-only forced-synthesis one, are gone. The engine's old internal `Typeface` is `LoadedTypeface`; a public `Typeface`
wraps it (`Typeface.Face` is that bridge, for the PDF writer, until export and metrics are public).

## Traps this turned up

- **`new TypefaceQuery()` is the normal query, `default` is not.** A positional `record struct`'s parameter defaults apply
  to a call that names the constructor's parameters, not to `new T()`, which is all zeros (weight 0, width class 0). The
  struct declares a parameterless constructor that chains to the primary one. A test that only checked "a regular face
  came back" passed with weight 0 by luck, since a lone face is the nearest one; the defaults test caught it.
- **A custom family's name keeps the spelling it was registered under.** `FontFamilyModel.Name` used to be the lowercased
  lookup key for a custom family, which is invisible inside the resolver and wrong for `TypefaceFamily.Name`. A family
  added with no name is still lowercase, because that is what the font reader's invariant-culture name is.
- **The last-resort family search must not be tested against private-use code points.** An installed font (Gabriola on
  Windows) covers U+E001, so a test that expected its own font to win failed on that machine. Use a range nothing
  installed covers (U+10FF00 to U+10FFF0) and a code point nothing covers (U+10FFFF).
- **Two visible differences, both in error paths.** A font that cannot be read now throws `TypefaceFormatException`
  (wrapping the reader's own exception) instead of whatever the reader threw, and the family is no longer registered with
  the adapter when its font failed to load. A host with no fonts at all throws `InvalidOperationException` from
  `MatchOrFallback` where the resolver threw `FileNotFoundException`. Nothing in the repo caught either.

## Not done

Metrics, glyph mapping, shaping, outlines and export still go through the internal `LoadedTypeface`/`OpenTypeDescriptor`.
That is the next slice, and the `InternalsVisibleTo` bridge stays until it and the export slice are in.
