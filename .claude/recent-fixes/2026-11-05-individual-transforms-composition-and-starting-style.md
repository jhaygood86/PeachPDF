# Individual transforms, animation-composition, transition-behavior, @starting-style, live @property

**What it is.**
- `translate`/`rotate`/`scale` are three JSON longhands (`VisualEffectsArea`, `InvalidateTransform`). They are composed as functions written *in front of* the transform list
  (`CssValueParser.EffectiveTransform`: `translate3d(..) rotate3d(..) scale3d(..) <transform>`), which is exactly the spec's order, and `DerivedStyle.HasNoTransformValue`/`HasTransform`
  look at all four, so stacking contexts, painting, retained layers and annotation placement follow with no other change (they all read the one matrix).
- `animation-composition` is applied where keyframes become frames: `AnimationApplier.BuildFrames` combines each keyframe value with the underlying one
  (`CssValueInterpolator.Composite`) before the mix, for the still frame and the live clock alike; a keyframe's own `animation-composition` is kept on `KeyframeStop.Composite`.
- `transition-behavior` is a longhand and a sixth slot of the `transition` shorthand; `AnimationClock.BeginTransitions` skips a property whose values flip (`IsDiscreteChange`) unless the entry
  says `allow-discrete`. `display` is always discrete and runs like a layout property.
- `@starting-style` is a grouping rule (`StartingStyleRule`) indexed with an `IsStartingStyle` flag that the ordinary cascade leaves out (`CssData.GatherMatchedRules`);
  `HtmlDocument.StartingStyles` cascades the tree again inside `CssData.WithStartingStyle`, keeps what each element got, and gives every box its own style back.
- A registered custom property (`@property`) with a mixing syntax is a live track: `AnimationClock` reads and writes it through `ReadProperty`/`WriteProperty` and cascades the box and its
  subtree again (the clock's `ApplyLiveValues` puts the value in the animation origin), so each `var()` reader follows.
- Transitions follow CSS Transitions 1 §3.4: a run that is still on its way to the value the style has now is left alone, and a change back to where it started reverses it over the part of
  the duration already travelled (`TransitionRun.ReversingAdjustedStart`/`ShorteningFactor`/`LastEased`).
- The `-webkit-` spellings of `animation*`/`transition*` are in `VendorPropertyAliases`, and `@-webkit-keyframes` in the at-rule dispatch.

**Found by running it, not by reading it.**
- An animated `color` never reached the text inside the box. The box's own style changed and the text boxes under it hold their own copy; the clock had only cascaded down for layout
  properties. It now cascades down for every inherited property it writes (`color`, `visibility` too) and, for a paint-only one, invalidates the paint caches instead of laying out.
- `PropertyFactory.IsAnimatable` tested the *shorthand's name* for each longhand, so `transition: margin 1s` was invalid; and a property that is never set reads back as nothing, so a transition
  from `margin-top`'s initial bare `0` to `40pt` fell through to the 50% flip (the interpolator now gives a bare `0` the other value's unit).
- A restyle for another reason (a reload while a width transition ran) read the half-way width back as the "old" value and started the transition again from there at full duration.
- `visibility` was classed as a layout property, so a hover on it reloaded the whole document; it only paints, and is now restyled in place with its subtree.

**Not done.** See [the motion path gaps](../accepted-gaps/css-motion-path-and-individual-transform-gaps.md) and [the live playback gaps](../accepted-gaps/css-animation-live-playback-limitations.md).
