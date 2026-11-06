# Mutation API and transitions from any restyle

**What it is.** `HtmlDocument.Mutation.cs`: `SetAttributeAsync` and the class helpers record the change in `_attributeChanges` (by the element's source ordinal, kept apart from `_formChanges`, which `ResetFormAsync` clears),
write it on the live `HtmlTag`, and run `RestyleAsync`, a reload that applies `AttributeOverridesForReload()` (form state plus these). `SetPreferencesAsync` sets the container's `Preferences`/`PreferredColorScheme` and does the same.
Transitions come from `ReloadAsync` itself: it keeps `SnapshotStyles` (the `ComputedStyle` of each element by ordinal, immutable, so readable after the old tree is disposed) before the reparse and `StartTransitionsFrom` diffs
each element (`InteractionRestyler.DiffStyles`) and calls `AnimationClock.BeginTransitions` before the layout, so a property starts laid out at its old value.

**Load-bearing ideas.**
- Reloading was chosen over restyling in place, on purpose. A class change can reach any rule, and the in-place machinery (`RecascadeForState` under `WithoutPseudoElementCreation`) cannot add or remove a pseudo-element, never
  sees a `display: contents` shell, and skips the one-shot passes (list markers, `::first-line`, anonymous boxes, word splitting). The cost is a parse per change; an in-place path for the cases it can do is a later optimisation.
- **The snapshot for a hover is taken before `InteractionRestyler.TryApply`**, not in `ReloadAsync`: the attempt recascades boxes in place and only then finds a layout property it cannot keep, so by the time of the reload the tree
  already holds the new style and nothing differs (`_styleSnapshot` hands it over).
- A property the style does not set reads back as nothing, so the diff uses `CssDefaults.GetInitialValue` (a bare `0` for `margin-top`), and `CssValueInterpolator` gives a bare `0` the unit of the value it is mixed with: before that
  `0` against `40pt` fell through to the 50% flip.
- `PropertyFactory.IsAnimatable` tested the shorthand's own name for each longhand (`_ => _animatables.Contains(name)`), so `transition: margin 1s` was invalid and never ran; a transition entry that is a shorthand now covers its
  longhands (`AnimationClock.NamesProperty`).
- A layout-property transition starts only if the last step of the clock was given a canvas (`_layoutCapable`): without one the clock would write the old value and never lay out the end value, leaving the box on it. The first
  step of a document with `AnimationsEnabled` is made at load by a host that animates, so the flag is known by the time the first mutation arrives.

**Not done.** Transitions of pseudo-elements, of inherited properties (`font-size`: the boxes under it need cascading again at the start), `transition-behavior`, `@starting-style`; an in-place path for mutations.
