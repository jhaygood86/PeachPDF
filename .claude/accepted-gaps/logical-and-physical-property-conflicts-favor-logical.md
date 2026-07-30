# Logical and physical properties on the same edge always favor the logical value

Tracking issue: [#546](https://github.com/jhaygood86/PeachPDF/issues/546).

CSS Logical Properties and Values Level 1 [§1.1](https://www.w3.org/TR/css-logical-1/#intro) makes a
logical longhand (e.g. `margin-inline-start`) and its physical counterpart on the same edge (e.g.
`margin-left`, under `direction: ltr`) conflict in the *ordinary* cascade — whichever is declared later
wins, exactly as for any two longhands that map to the same effective property.

`CssBox.ResolveLogicalProperties()` (`src/PeachPDF/Html/Core/Dom/CssBox.LogicalProperties.cs`) does not
track declaration order between a logical value and its physical sibling. It runs unconditionally, once,
after the rest of the cascade has already applied both values — so a cascaded logical value always
overwrites whatever a physical declaration on the same edge already wrote, regardless of which one was
actually declared later. `div { margin-inline-start: 10pt; margin-left: 20pt; }` resolves to a 10pt left
margin here; per spec (with `direction: ltr`, so both target the same edge), `margin-left: 20pt` is
declared later and should win, giving 20pt.

**Deliberately out of scope.** This is a narrow, low-likelihood conflict — authors rarely mix logical and
physical longhands for the same edge on the same box — and true declaration-order tracking would require
plumbing per-declaration cascade order through to this resolution step, which does not otherwise need it
anywhere else in `CssBox`'s cascade. Taken as an explicit, known simplification when direction/writing-mode-aware
logical property resolution was implemented (`LogicalPropertyResolver`), rather than building that
machinery for one narrow interaction.

The reader-facing note is in `docs/html-css-support.md`'s CSS Logical Properties section.
