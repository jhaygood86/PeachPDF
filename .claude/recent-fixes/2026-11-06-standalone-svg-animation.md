# Standalone SVG images play like animated images; transform set/animate; paused SVG animations

**What it is.**
- `StandaloneSvgPlayer` (`Svg/`) holds what is needed to build a standalone SVG's scene graph again: its XML root, `CssData`, var context, prefetched images, the `SmilSet` and the keyframes of its own
  `<style>`. `ImageLoadHandler.LoadSvgFromStream` makes one (when `AnimationsEnabled` and something animates) and hangs it on the cached `SvgDocument.Player`; `SvgDocument.Current` is the
  player's latest document, and the three painters (`ImageFragmentPainter`, `ObjectFragmentPainter`, `CssImagePainter`) paint `.Current`. The resource cache holds one `SvgDocument` per address, so
  every box that shows the file shows the same moment, as for a GIF's frame.
- The clock steps a player once per frame for all the boxes that show it (`playerSteps` in `AnimationClock.Advance`), on the time since the image first showed (`_playerStart`), registers a `BoxState`
  for each such box (`PlayerOf`) and adds the box to `_changed` when the document changed, so the dirty regions are the image boxes' own rectangles.
- SMIL is read through `ISmilNode` (`CssBoxSmilNode`, `XElementSmilNode`); targets are keyed by `ISmilNode.Key` (the box or the `XElement`). `XElementSvgSourceNode` got the same seams as the
  inline node (`GetAttribute`, `IsAnimated`, `GetAnimatedValue`, and `Parent`/`IdentityKey` only while it is animated, since they matter only to the animation lookup).
- CSS animations of a standalone SVG: `AnimationApplier.PlanStandaloneElement` / `StandaloneAnimationEnd` read the animation longhands through a `Func<string,string?>` over the element's matched
  declarations (they are expanded longhands: `animation: k 2s` arrives as `animation-name`, `animation-duration`...). `BuildPlan` is the one loop for a box and for a standalone element.
  `SampleElement`/`Sample`/`ResolveVariables` take a null box; `var()` in a keyframe then drops the declaration.
- `animation-play-state: paused` on an inline or standalone SVG element holds its time at zero (the elements are not restyled while it plays, so it cannot be resumed); a paused animation ends nothing.
- SMIL `animate`/`set` of the `transform` attribute go through `SampleTransform` with `animateTransform` and `animateMotion`.

**Load-bearing.** A standalone SVG's `data:` image needs the host's `ResourceLoader` to load at all (the PeachBrackets tests set it, as the GIF tests do): a white box in a test is a missing loader,
not a bug in the player.

**Not done.** Changed-element dirty regions: see the live playback gaps. A background-image SVG is not played.
