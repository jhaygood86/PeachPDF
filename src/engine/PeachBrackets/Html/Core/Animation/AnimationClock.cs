using System;
using System.Collections.Generic;
using System.Numerics;
using PeachBrackets.Css;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Parse;
using PeachBrackets.Html.Fragments;
using PeachBrackets.Html.Paint;
using PeachBrackets.Html.TextGeometry;
using PeachBrackets.Html.Updates;
using PeachBrackets.Html.Utils;
using PeachBrackets.Updates;
using PeachDrawing.Core;

namespace PeachBrackets.Html.Animation
{
    /// <summary>
    /// The running clock of a document that plays its animations and transitions (<c>HtmlDocumentOptions.AnimationsEnabled</c>). A PDF is one
    /// moment and has no clock; a document on a screen has one, and each frame this samples every running animation and transition at the
    /// current time and writes the result onto the boxes that own them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only properties that cannot change layout are written (see <see cref="InteractionRestyler.IsPaintOnly"/>): a frame runs no layout pass
    /// and no cascade, so it costs the sampling, and the repaint of what changed. Of those, <c>transform</c> and <c>opacity</c> are the ones a
    /// compositor can animate without repainting the subtree at all: a box that animates them (and has no animated box inside it) is
    /// <see cref="IsPromoted">promoted</see>, and the painter keeps what it paints as a <see cref="RetainedLayer"/> that a frame only draws again
    /// with the new matrix and opacity.
    /// </para>
    /// <para>
    /// The boxes can be replaced under the clock (a document loaded again) or restyled in place. The clock follows both: runs are kept by the
    /// element's place in the source and the animation's name, so a reload keeps an animation where it was; and after an in-place restyle
    /// it takes the values a box was given back from the cascade and writes the running ones over them again.
    /// </para>
    /// </remarks>
    internal sealed class AnimationClock : IDisposable
    {
        private readonly HtmlContainerInt _container;
        private readonly CssValueParser _parser;
        private readonly List<BoxState> _states = [];
        private readonly Dictionary<CssBox, BoxState> _byBox = [];
        private readonly Dictionary<(int Ordinal, string Name, int Index), (AnimationApplier.LiveTrack Track, CssBox Box)> _carried = [];
        private readonly Dictionary<(int Ordinal, string Property), TransitionRun> _transitions = [];
        private readonly List<Image> _images = [];
        private readonly Dictionary<Image, double> _imageElapsed = [];
        private CssBox? _root;
        private int _generation = -1;
        private int _imageGeneration = -1;
        private double _last = double.NaN;
        private int _contentGeneration;
        private bool _disposed;

        internal AnimationClock(HtmlContainerInt container)
        {
            _container = container;
            _parser = new CssValueParser(container.RenderContext);
        }

        /// <summary>How many layers the painter has recorded; a promoted box that is only moving or fading adds none after its first frame. For tests.</summary>
        public int RecordedLayers { get; internal set; }

        /// <summary>Whether anything is still running: a frame will change something unless this is false.</summary>
        public bool IsRunning { get; private set; }

        /// <summary>Whether a step is due: something is running, or an image has arrived that may be animated and has not been looked at yet.</summary>
        public bool NeedsStep => IsRunning || _scrollDirty || _events.Count > 0 || _imageGeneration != _container.ImageGeneration;

        /// <summary>Increases whenever what a promoted box paints may have changed, so a layer recorded earlier is stale.</summary>
        public int ContentGeneration => _contentGeneration;

        /// <summary>Marks every retained layer stale: something a layer holds was repainted for a reason other than the animation.</summary>
        public void InvalidateContent() => _contentGeneration++;

        /// <summary>
        /// Whether the clock is cascading boxes again itself, so the cascade must give them the values the clock holds (see <see cref="ApplyLiveValues"/>). It is not
        /// the case for any other cascade: a restyle for a hover state starts from the rules, and the clock reads what it left behind afterwards.
        /// </summary>
        public bool HasOverrides => _recascading;

        private bool _recascading;

        /// <summary>
        /// Writes the values this clock last gave <paramref name="box"/> onto it. The cascade calls this where the animation origin sits, for a box it cascades
        /// again while an animation runs (an inherited property animating, such as <c>font-size</c>, is cascaded down the boxes under the animated one).
        /// </summary>
        public void ApplyLiveValues(CssValueParser parser, CssBox box)
        {
            if (!_byBox.TryGetValue(box, out var state) || state.Written.Count == 0) return;
            foreach (var (property, value) in state.Written)
            {
                if (value is not null) WriteProperty(parser, box, property, value);
            }
        }

        /// <summary>Whether the painter should keep <paramref name="box"/>'s paint as a retained layer.</summary>
        public bool IsPromoted(CssBox box) => _byBox.Count > 0 && _byBox.TryGetValue(box, out var state) && state.Promoted;

        /// <summary>The retained layer of a promoted box, for the painter to fill or draw.</summary>
        public RetainedEntry? EntryFor(CssBox box) => _byBox.TryGetValue(box, out var state) && state.Promoted ? state.Entry : null;

        /// <summary>The layer a promoted box's paint is kept in, with what it was recorded for.</summary>
        public sealed class RetainedEntry
        {
            public RetainedLayer? Layer { get; set; }
            public int Generation { get; set; } = -1;
            public double Scale { get; set; }
            public Rect Bounds { get; set; }

            /// <summary>The fragment the layer was recorded for: a box split across pages has a fragment on each, and a layer is only good for its own.</summary>
            public object? Fragment { get; set; }

            public void Discard()
            {
                Layer?.Dispose();
                Layer = null;
                Generation = -1;
            }
        }

        private sealed class BoxState
        {
            public required CssBox Box { get; init; }
            public int Ordinal { get; init; } = -1;
            public List<AnimationApplier.LiveTrack> Tracks { get; set; } = [];
            public List<TransitionRun> Transitions { get; } = [];
            public Dictionary<string, string?> Base { get; } = new(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, string?> Written { get; } = new(StringComparer.OrdinalIgnoreCase);
            public ComputedStyle? StyleAfterWrite { get; set; }
            public bool Promoted { get; set; }
            public bool Compositor { get; set; }
            public RetainedEntry Entry { get; } = new();
            public string[] Properties { get; set; } = [];
            public List<FragmentPart> Fragments { get; } = [];
            public Matrix3x2 LastMatrix { get; set; } = Matrix3x2.Identity;
            public bool LastHasTransform { get; set; }
            public bool BleedStale { get; set; }
        }

        internal sealed class TransitionRun
        {
            public required CssBox Box { get; set; }
            public required int Ordinal { get; init; }
            public required string Property { get; init; }
            public required string From { get; init; }
            public required string To { get; init; }
            public required double DelaySeconds { get; init; }
            public required double DurationSeconds { get; init; }
            public required EasingFunction Easing { get; init; }
            public double Elapsed { get; set; }
            public bool Finished { get; set; }

            /// <summary>The value the transition ran from, or for a reversed one the end of the transition it reversed: what a change back to it is measured against (CSS Transitions 1 §3.4).</summary>
            public string? ReversingAdjustedStart { get; init; }

            /// <summary>How much of the duration a reversed transition has been cut down to; 1 for one that is not.</summary>
            public double ShorteningFactor { get; init; } = 1;

            /// <summary>The output of the timing function at the last step: where the transition was on its way when it is interrupted.</summary>
            public double LastEased { get; set; }

            /// <summary>Whether <c>transitionstart</c> has been sent.</summary>
            public bool Started { get; set; }

            /// <summary>Cancelled through <see cref="DocumentAnimation.Cancel"/>: it ends with a cancel event, and the property has its new value.</summary>
            public bool Cancelled { get; set; }

            /// <summary>Taken away: replaced by a newer transition of the property, or its element is gone.</summary>
            public bool Removed { get; set; }

            /// <summary>Held by <see cref="DocumentAnimation.Pause"/>.</summary>
            public bool ControlPaused { get; set; }

            /// <summary>How fast time passes for the transition (<see cref="DocumentAnimation.PlaybackRate"/>).</summary>
            public double PlaybackRate { get; set; } = 1;

            /// <summary>The object the host holds for this transition, once it has asked for one.</summary>
            public DocumentAnimation? Handle { get; set; }
        }

        /// <summary>
        /// Starts a transition for each property in <paramref name="changes"/> that the box's <c>transition-*</c> properties name, writing the old
        /// value back so the box starts from it. Called by the in-place restyle after it re-cascaded the box and found these properties different.
        /// </summary>
        /// <param name="box">the restyled box</param>
        /// <param name="changes">each property that changed, with the value it had and the value the cascade gave it</param>
        public void BeginTransitions(CssBox box, IReadOnlyList<(string Property, string? Old, string? New)> changes)
        {
            if (_disposed) return;

            var properties = AnimationApplier.SplitList(box.TransitionProperty);
            if (properties.Count == 0) return;
            var durations = AnimationApplier.SplitList(box.TransitionDuration);
            var delays = AnimationApplier.SplitList(box.TransitionDelay);
            var timings = AnimationApplier.SplitList(box.TransitionTimingFunction);
            var behaviors = AnimationApplier.SplitList(box.TransitionBehavior);
            var ordinal = box.HtmlTag?.SourceOrdinal ?? -1;

            var layoutWritten = false;
            foreach (var (property, oldValue, newValue) in changes)
            {
                if (oldValue is null || newValue is null || oldValue == newValue) continue;

                // A property that moves or resizes boxes runs only when the host lays the document out for each frame, and one that boxes inherit
                // would need the boxes under it cascaded again at the start, which a style change that is already settled cannot do.
                // display is discrete and changes what is laid out, so it runs like a layout property.
                var paintOnly = InteractionRestyler.IsPaintOnly(property);
                var isDisplay = property.Equals("display", StringComparison.OrdinalIgnoreCase);
                if (!paintOnly && !(_layoutCapable && (isDisplay || (InteractionRestyler.IsLiveAnimatable(property) && !InteractionRestyler.InheritsToDescendants(property))))) continue;

                // A running animation of the property decides its value, not a transition (CSS Transitions 1 §3).
                if (ordinal >= 0 && AnimatesProperty(ordinal, property)) continue;

                // The index of the entry that names the property (`all`, the property itself or a shorthand that covers it), taken from the last one
                // that does (the later entry wins).
                var index = -1;
                for (var i = 0; i < properties.Count; i++)
                {
                    if (NamesProperty(properties[i].Trim(), property)) index = i;
                }

                if (index < 0) continue;

                // CSS Transitions 2 §2.5: a property whose values do not mix (display, a keyword) only transitions when its entry says allow-discrete.
                if (IsDiscreteChange(property, oldValue, newValue, box)
                    && !AnimationApplier.LiveCycle(behaviors, index).Trim().Equals("allow-discrete", StringComparison.OrdinalIgnoreCase))
                    continue;

                var duration = AnimationApplier.LiveSeconds(AnimationApplier.LiveCycle(durations, index));
                var delay = AnimationApplier.LiveSeconds(AnimationApplier.LiveCycle(delays, index));
                if (duration + Math.Max(delay, 0) <= 0) continue;

                var key = (ordinal, property);
                var reversingStart = oldValue;
                var factor = 1.0;
                if (_transitions.TryGetValue(key, out var superseded))
                {
                    // Still on its way to the value the style has now: nothing changed for this property, and the transition runs on (a restyle for another reason
                    // reads the half-way value back as the old one).
                    if (!superseded.Finished && SameValue(superseded.To, newValue)) continue;

                    // CSS Transitions 1 §3.4: a change back to where the running transition started reverses it, over the part of the duration already travelled.
                    if (!superseded.Finished && SameValue(superseded.ReversingAdjustedStart, newValue))
                    {
                        factor = Math.Clamp(Math.Abs((superseded.LastEased * superseded.ShorteningFactor) + (1 - superseded.ShorteningFactor)), 0, 1);
                        reversingStart = superseded.To;
                    }

                    RetireRun(superseded);
                    superseded.Finished = true;
                    if (_byBox.TryGetValue(superseded.Box, out var oldState)) oldState.Transitions.Remove(superseded);
                }

                // The delay of a reversed transition is cut down with its duration when it is negative.
                var effectiveDelay = delay >= 0 ? delay : delay * factor;
                var effectiveDuration = duration * factor;

                // A transition already running from some value goes on from where it is: the old value read back is what is on the box now.
                var created = _transitions[key] = new TransitionRun
                {
                    Box = box,
                    Ordinal = ordinal,
                    Property = property,
                    From = oldValue,
                    To = newValue,
                    DelaySeconds = effectiveDelay,
                    DurationSeconds = effectiveDuration,
                    Easing = EasingFunction.TryParse(AnimationApplier.LiveCycle(timings, index), out var parsed) ? parsed : EasingFunction.Ease,
                    ReversingAdjustedStart = reversingStart,
                    ShorteningFactor = factor,
                };

                Emit(AnimationEventKind.TransitionRun, box, property, 0);

                // Start at the old value; the next frame mixes toward the new one.
                CssPropertyRegistry.TrySet(_parser, box, property, oldValue);
                layoutWritten |= !paintOnly;
            }

            // What was worked out from the new value is made again from the old one by the layout pass the caller runs next.
            if (layoutWritten) box.ResetDerivedStyle();

            _generation = -1; // bind again: the style changed under us
            IsRunning = true;
        }

        // A custom property has no entry in the property registry: it is in the box's own dictionary, or has its registered initial value.
        private static string? ReadProperty(CssBox box, string property)
        {
            if (!PropertyFactory.IsCustomPropertyName(property)) return CssUtils.GetPropertyValue(box, property);

            if (box.CustomProperties is { } custom && custom.TryGetValue(property, out var value)) return value;

            RegisteredProperty? registered = null;
            box.HtmlContainer?.RegisteredProperties?.TryGetValue(property, out registered);
            return registered?.InitialValue;
        }

        // The dictionary is shared by boxes that were given the same style, so a changed one is a copy.
        private static bool WriteProperty(CssValueParser parser, CssBox box, string property, string value)
        {
            if (!PropertyFactory.IsCustomPropertyName(property)) return CssPropertyRegistry.TrySet(parser, box, property, value);

            var copy = box.CustomProperties is { } existing ? new Dictionary<string, string>(existing) : new Dictionary<string, string>();
            copy[property] = value;
            box.CustomProperties = copy;
            return true;
        }

        private static bool SameValue(string? a, string? b) => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

        // Whether the two values of a property do not mix: the interpolator gives one end for the first half and the other for the second. display always counts
        // (none against a box is visible for the whole of the way, a step); visibility mixes on its own rule, and is not discrete.
        private bool IsDiscreteChange(string property, string from, string to, CssBox box)
        {
            if (property.Equals("display", StringComparison.OrdinalIgnoreCase)) return true;
            if (property.Equals("visibility", StringComparison.OrdinalIgnoreCase)) return false;

            var early = AnimationApplier.MixTransition(_parser, box, property, from, to, 0.25);
            var late = AnimationApplier.MixTransition(_parser, box, property, from, to, 0.75);
            return SameValue(early, from) && SameValue(late, to);
        }

        // Whether a transition-property entry covers a property: `all`, the property itself, or a shorthand that sets it (CSS Transitions 1 §2.1).
        private static bool NamesProperty(string entry, string property) =>
            entry.Equals(property, StringComparison.OrdinalIgnoreCase) || entry.Equals("all", StringComparison.OrdinalIgnoreCase)
            || (PropertyFactory.Instance.IsShorthand(entry) && Array.Exists(PropertyFactory.Instance.GetLonghands(entry), longhand => longhand.Equals(property, StringComparison.OrdinalIgnoreCase)));

        // Whether a running animation of the element with this source ordinal animates the property.
        private bool AnimatesProperty(int ordinal, string property)
        {
            foreach (var state in _states)
            {
                if (state.Ordinal != ordinal) continue;
                foreach (var track in state.Tracks)
                {
                    if (!track.Finished && track.HasProperty(property)) return true;
                }
            }

            return false;
        }

        // Whether the host that steps the clock lays the document out for each frame (it passed a canvas), as of the last step.
        private bool _layoutCapable;

        // The events that happened since the last step reported them, in order.
        private readonly List<AnimationEventInfo> _events = [];

        #region Scroll-driven animations

        // How the document is scrolled, in points, as the host last said (HtmlDocument feeds it before each step): the visible part, and how big the whole surface is.
        private Rect _viewport;
        private double _surfaceWidth;
        private double _surfaceHeight;
        private bool _scrollDirty;

        // Where each box that a view timeline follows is, on the surface, in points; made again for each layout.
        private readonly Dictionary<CssBox, Rect> _subjectRects = new(ReferenceEqualityComparer.Instance as IEqualityComparer<CssBox>);
        private IReadOnlyList<FragmentainerFragment>? _subjectsFor;

        /// <summary>Tells the clock how the document is scrolled. The animations that run on a scroll or view timeline are worked out from it at the next step.</summary>
        internal void SetScroll(Rect viewport, double surfaceWidth, double surfaceHeight)
        {
            _viewport = viewport;
            _surfaceWidth = surfaceWidth;
            _surfaceHeight = surfaceHeight;
        }

        /// <summary>The document was scrolled: a step is due, once, for whatever runs on a scroll or view timeline (nothing is running in the sense of time).</summary>
        internal void NotifyViewportChanged()
        {
            if (HasScrollDrivenTracks()) _scrollDirty = true;
        }

        private bool HasScrollDrivenTracks()
        {
            foreach (var state in _states)
            {
                foreach (var track in state.Tracks)
                {
                    if (track.IsScrollDriven && !track.Removed) return true;
                }
            }

            return false;
        }

        // How far through its range the animation's timeline is, as the scroll position and the subject's place say now (Scroll-driven Animations 1 §4.4).
        private void UpdateScrollTrack(CssBox box, AnimationApplier.LiveTrack track, IReadOnlyList<FragmentainerFragment> fragmentainers)
        {
            track.ScrollProgress = track.Timeline is { } timeline
                ? AnimationTimeline.ScrollProgress(RawScrollProgress(box, track, timeline, fragmentainers), track.IterationCount, track.Direction, track.FillsBackwards, track.FillsForwards)
                : null;
        }

        private double? RawScrollProgress(CssBox box, AnimationApplier.LiveTrack track, ResolvedTimeline timeline, IReadOnlyList<FragmentainerFragment> fragmentainers)
        {
            var vertical = timeline.Axis is "block" or "y";
            var position = vertical ? _viewport.Y : _viewport.X;
            var size = vertical ? _viewport.Height : _viewport.Width;
            var total = vertical ? _surfaceHeight : _surfaceWidth;
            if (!(size > 0)) return null;

            if (!timeline.IsView)
            {
                // The range is how far the document can scroll, unless the animation names a part of it.
                var maxScroll = Math.Max(total - size, 0);
                var scrollStart = track.RangeStart.IsNormal ? 0 : ScrollOffset(track.RangeStart, maxScroll, box);
                var scrollEnd = track.RangeEnd.IsNormal ? maxScroll : ScrollOffset(track.RangeEnd, maxScroll, box);
                return Fraction(position, scrollStart, scrollEnd);
            }

            if (timeline.Subject is not { } subject || SubjectRect(subject, fragmentainers) is not { } rect) return null;

            var s = vertical ? rect.Y : rect.X;
            var length = vertical ? rect.Height : rect.Width;

            // The scrollport is shrunk by the inset at each end (view-timeline-inset); a percentage is of the scrollport.
            var (insetStart, insetEnd) = ("auto", "auto");
            if (timeline.Inset is { } inset && TimelineGrammar.TryParseInset(inset, out var start, out var end)) (insetStart, insetEnd) = (start, end);
            var a = InsetPoints(insetStart, size, subject);
            var b = InsetPoints(insetEnd, size, subject);

            var rangeStart = ViewOffset(track.RangeStart, isEnd: false, s, length, size, a, b, subject);
            var rangeEnd = ViewOffset(track.RangeEnd, isEnd: true, s, length, size, a, b, subject);
            return Fraction(position, rangeStart, rangeEnd);
        }

        private static double? Fraction(double position, double start, double end)
        {
            if (!(end > start)) return position >= end ? 1 : 0;
            return (position - start) / (end - start);
        }

        // An offset of a scroll timeline's range: a percentage of the whole range, or a length from its start.
        private double ScrollOffset(RangeEdge edge, double maxScroll, CssBox box) =>
            edge.Offset is { } offset ? LengthPoints(offset, maxScroll, box) : 0;

        private double InsetPoints(string inset, double scrollport, CssBox box) =>
            inset.Equals("auto", StringComparison.OrdinalIgnoreCase) ? 0 : LengthPoints(inset, scrollport, box);

        // A <length-percentage> in points; a percentage is of the basis.
        private double LengthPoints(string text, double basisPoints, CssBox box)
        {
            text = text.Trim();
            if (text.EndsWith('%') && double.TryParse(text.AsSpan(0, text.Length - 1), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var percent))
                return basisPoints * percent / 100;

            var ppp = _container.RenderContext.LayoutUnitsPerPoint;
            return CssValueParser.ParseLength(text, basisPoints * ppp, box) / ppp;
        }

        // The scroll positions at which a named range of a view timeline starts and ends (Scroll-driven Animations 1 §4.3.1), for a subject that starts at s and is
        // `length` long on a scrollport of `size` whose ends are inset by a and b.
        private static (double Start, double End) RangeName(string name, double s, double length, double size, double a, double b)
        {
            var coverStart = s - size + b;
            var coverEnd = s + length - a;
            var containA = s + length - size + b;
            var containB = s - a;
            var containStart = Math.Min(containA, containB);
            var containEnd = Math.Max(containA, containB);

            return name switch
            {
                "contain" => (containStart, containEnd),
                "entry" => (coverStart, containStart),
                "exit" => (containEnd, coverEnd),
                "entry-crossing" => (coverStart, s + length - size + b),
                "exit-crossing" => (s - a, coverEnd),
                _ => (coverStart, coverEnd),
            };
        }

        // A scroll position that one end of animation-range means in a view timeline. normal is the whole cover range; an offset alone is into the cover range; a name is
        // into that range, 0% at its start and 100% at its end when no offset follows it.
        private double ViewOffset(RangeEdge edge, bool isEnd, double s, double length, double size, double a, double b, CssBox box)
        {
            var named = RangeName(edge.Name ?? "cover", s, length, size, a, b);
            if (edge.IsNormal) return isEnd ? named.End : named.Start;
            if (edge.Offset is null) return isEnd ? named.End : named.Start;

            return named.Start + LengthPoints(edge.Offset, named.End - named.Start, box);
        }

        private Rect? SubjectRect(CssBox subject, IReadOnlyList<FragmentainerFragment> fragmentainers)
        {
            if (!ReferenceEquals(_subjectsFor, fragmentainers))
            {
                _subjectsFor = fragmentainers;
                _subjectRects.Clear();

                var ppp = _container.RenderContext.LayoutUnitsPerPoint;
                for (var page = 0; page < fragmentainers.Count; page++)
                {
                    var geometry = fragmentainers[page].Geometry;
                    var frame = new TextRunCollector.Frame(geometry.ContentLeftPt, geometry.ContentTopPt, _container.MarginLeft, _container.MarginTop, ppp);
                    Index(fragmentainers[page].Root, frame);
                }
            }

            return _subjectRects.TryGetValue(subject, out var rect) ? rect : null;

            void Index(BoxFragment fragment, TextRunCollector.Frame frame)
            {
                var whole = fragment.WholeBoxRect;
                _subjectRects.TryAdd(fragment.Box, new Rect(frame.X(whole.X), frame.Y(whole.Y), whole.Width / frame.Ppp, whole.Height / frame.Ppp));
                foreach (var child in fragment.Children) Index(child, frame);
            }
        }

        #endregion

        private void Emit(AnimationEventKind kind, CssBox box, string name, double elapsedSeconds) =>
            _events.Add(new AnimationEventInfo(kind, new DocumentElement(box), name, TimeSpan.FromSeconds(Math.Max(0, elapsedSeconds))));

        // animationstart, animationiteration and animationend for what the animation has got through since the last step (CSS Animations 1 §4.2).
        private void EmitTrackEvents(CssBox box, AnimationApplier.LiveTrack track)
        {
            if (track.Cancelled || track.Removed) return;

            var delay = track.DelaySeconds;
            var active = track.ActiveSeconds;
            var elapsed = track.Elapsed;

            if (!track.Started)
            {
                if (elapsed < delay) return;

                track.Started = true;
                Emit(AnimationEventKind.AnimationStart, box, track.Name, Math.Clamp(-delay, 0, active));
            }

            var duration = track.DurationSeconds;
            if (duration > 0 && elapsed >= delay && !track.Ended)
            {
                var current = (int)Math.Min(Math.Floor((elapsed - delay) / duration), int.MaxValue - 1);
                var last = double.IsInfinity(active) ? int.MaxValue : (int)Math.Ceiling(active / duration) - 1;
                var target = Math.Min(Math.Min(current, last), track.Iteration + 1000);
                while (track.Iteration < target)
                {
                    track.Iteration++;
                    Emit(AnimationEventKind.AnimationIteration, box, track.Name, track.Iteration * duration);
                }
            }

            if (!track.Ended && !double.IsInfinity(active) && elapsed - delay >= active)
            {
                track.Ended = true;
                Emit(AnimationEventKind.AnimationEnd, box, track.Name, active);
                track.Handle?.Complete(cancelled: false);
            }
        }

        // transitionstart once the delay is over (CSS Transitions 1 §6).
        private void EmitRunStart(TransitionRun run)
        {
            if (run.Started || run.Removed || run.Cancelled || run.Elapsed < run.DelaySeconds) return;

            run.Started = true;
            Emit(AnimationEventKind.TransitionStart, run.Box, run.Property, Math.Clamp(-run.DelaySeconds, 0, run.DurationSeconds));
        }

        // A transition that stops before its end: replaced by a newer one, or its element is gone.
        private void RetireRun(TransitionRun run)
        {
            if (run.Removed) return;

            run.Removed = true;
            if (!run.Finished)
                Emit(AnimationEventKind.TransitionCancel, run.Box, run.Property, Math.Clamp(run.Elapsed - run.DelaySeconds, 0, run.DurationSeconds));

            run.Handle?.Complete(cancelled: !run.Finished);
        }

        /// <summary>
        /// Moves the clock to <paramref name="nowSeconds"/> (any monotonic time), writes what every running animation and transition is at then, and
        /// says which regions of which pages changed.
        /// </summary>
        /// <param name="nowSeconds">the time, in seconds, from any monotonic clock</param>
        /// <param name="fragmentainers">the pages as they were last laid out</param>
        /// <param name="allowLayout">whether the caller will lay the document out again after this step; only then are properties that change layout written
        /// (the result says <see cref="AnimationFrame.LayoutChanged"/> when one was)</param>
        public AnimationFrame Advance(double nowSeconds, IReadOnlyList<FragmentainerFragment> fragmentainers, bool allowLayout = false)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            var dt = double.IsNaN(_last) ? 0 : Math.Max(0, nowSeconds - _last);
            _last = nowSeconds;

            Bind();
            EnsureExtents(fragmentainers);
            _scrollDirty = false;

            // A page of container queries lays itself out through a new cascade, which replaces the boxes this writes to; one that is paged has no
            // single surface to resize. Neither can follow a layout property frame by frame.
            allowLayout = allowLayout && !_container.HasSizeContainers && _container.Continuous;
            _layoutCapable = allowLayout;

            _changed.Clear();
            var stackingFlip = false;
            var running = false;
            var layoutBoxes = allowLayout ? new List<CssBox>() : null;
            List<CssBox>? cascadeBoxes = null;

            foreach (var state in _states)
            {
                var box = state.Box;

                foreach (var track in state.Tracks)
                {
                    // On a scroll or view timeline the position says where it is: no time passes for it and it reports no events.
                    if (track.IsScrollDriven)
                    {
                        if (!track.Paused && !track.ControlPaused && !track.Cancelled) UpdateScrollTrack(box, track, fragmentainers);
                        continue;
                    }

                    if (!track.Cancelled && !track.Paused && !track.ControlPaused && !(track.Finished && track.PlaybackRate >= 0))
                        track.Elapsed = Math.Max(0, track.Elapsed + (dt * track.PlaybackRate));

                    EmitTrackEvents(box, track);
                }

                foreach (var run in state.Transitions)
                {
                    if (!run.Finished && !run.ControlPaused) run.Elapsed = Math.Max(0, run.Elapsed + (dt * run.PlaybackRate));
                    EmitRunStart(run);
                }

                if (state.Tracks.Count == 0 && state.Transitions.Count == 0) continue;

                var wrote = false;
                foreach (var property in state.Properties)
                {
                    var paintOnly = InteractionRestyler.IsPaintOnly(property);
                    if (!paintOnly && !allowLayout) continue;

                    var value = state.Base.TryGetValue(property, out var baseValue) ? baseValue : null;

                    foreach (var run in state.Transitions)
                    {
                        if (!run.Property.Equals(property, StringComparison.OrdinalIgnoreCase)) continue;
                        value = SampleTransition(run);
                    }

                    foreach (var track in state.Tracks)
                    {
                        if (track.Cancelled || !track.HasProperty(property)) continue;
                        if (AnimationApplier.SampleLive(_parser, box, track, property, value) is { } sampled) value = sampled;
                    }

                    if (value is null) continue;
                    if (state.Written.TryGetValue(property, out var last) && last == value) continue;

                    // A layout pass for a value the box already has would only be the cost of one.
                    if (!paintOnly && ReadProperty(box, property) == value)
                    {
                        state.Written[property] = value;
                        continue;
                    }

                    if (!WriteProperty(_parser, box, property, value)) continue;

                    if (IsStackingProperty(property) && IsNeutral(property, state.Written.GetValueOrDefault(property) ?? state.Base.GetValueOrDefault(property)) != IsNeutral(property, value))
                        stackingFlip = true;

                    state.Written[property] = value;
                    wrote = true;
                    if (!IsCompositorProperty(property)) state.BleedStale = true;

                    if (!paintOnly) layoutBoxes!.Add(box);

                    // A property the boxes under this one inherit (font-size, but also color and visibility, which only paint) has to be cascaded down to them: they hold
                    // their own copy of it.
                    // A registered custom property is read through var() by the box and by any box under it, so it goes down the same way.
                    if (InteractionRestyler.InheritsToDescendants(property) || PropertyFactory.IsCustomPropertyName(property)) (cascadeBoxes ??= []).Add(box);
                }

                if (wrote)
                {
                    InteractionRestyler.InvalidatePaintCaches(box);
                    state.StyleAfterWrite = box.ComputedStyle;
                    _changed.Add(state);
                }

                foreach (var track in state.Tracks)
                {
                    if (!track.IsScrollDriven && !track.Paused && !track.ControlPaused && !track.Cancelled && (!track.Finished || track.PlaybackRate < 0)) running = true;
                }
            }

            // A transition that has reached its end has written its final value above; it is done.
            List<(int, string)>? done = null;
            foreach (var (key, run) in _transitions)
            {
                if (run.Elapsed >= run.DelaySeconds + run.DurationSeconds)
                {
                    run.Finished = true;
                    (done ??= []).Add(key);
                }
                else if (!run.ControlPaused)
                {
                    running = true;
                }
            }

            if (done is not null)
            {
                foreach (var key in done)
                {
                    var run = _transitions[key];
                    if (_byBox.TryGetValue(run.Box, out var state)) state.Transitions.Remove(run);
                    _transitions.Remove(key);

                    // A transition that reached its end says so (a cancelled one has already been told to stop and says that).
                    EmitRunStart(run);
                    if (run.Cancelled)
                    {
                        run.Removed = true;
                        Emit(AnimationEventKind.TransitionCancel, run.Box, run.Property, Math.Clamp(run.Elapsed - run.DelaySeconds, 0, run.DurationSeconds));
                        run.Handle?.Complete(cancelled: true);
                    }
                    else
                    {
                        Emit(AnimationEventKind.TransitionEnd, run.Box, run.Property, run.DurationSeconds);
                        run.Handle?.Complete(cancelled: false);
                    }
                }

                _generation = -1; // promotion changes when a transition ends
            }

            if (stackingFlip) _container.RefreshStackingHoistCandidates();

            var layoutChanged = layoutBoxes is { Count: > 0 };
            if (layoutChanged || cascadeBoxes is { Count: > 0 }) PrepareLayout(layoutBoxes ?? [], cascadeBoxes, layoutChanged);

            var dirty = new List<DirtyRegion>();

            // A layout pass moves things the old extents describe, and the caller repaints whole pages for it: nothing to work out per box.
            if (_changed.Count > 0 && !layoutChanged) AddDirtyRegions(dirty);

            running |= AdvanceImages(dt, fragmentainers, dirty);

            IsRunning = running;

            var events = _events.Count == 0 ? (IReadOnlyList<AnimationEventInfo>)[] : _events.ToArray();
            _events.Clear();
            return new AnimationFrame(running, dirty, layoutChanged) { Events = events };
        }

        // The boxes were written to in place; what is worked out from their style, and the styles of the boxes that inherit from them, is made again by
        // the layout pass that follows.
        private void PrepareLayout(List<CssBox> written, List<CssBox>? inherited, bool layout)
        {
            _contentGeneration++;
            if (layout) _extentsStale = true;

            var reset = new HashSet<CssBox>();
            if (inherited is not null)
            {
                var roots = new HashSet<CssBox>(inherited);
                _recascading = true;
                try
                {
                    foreach (var box in inherited)
                    {
                        var covered = false;
                        for (var ancestor = box.ParentBox; ancestor is not null && !covered; ancestor = ancestor.ParentBox) covered = roots.Contains(ancestor);
                        if (covered) continue;

                        DomParser.RecascadeForState(_container, box);
                        Collect(box);
                    }
                }
                finally
                {
                    _recascading = false;
                }
            }

            foreach (var box in written) reset.Add(box);
            if (inherited is not null) foreach (var box in inherited) reset.Add(box);

            // A change that only paints keeps the measured text and the fonts; one that lays out starts from the style again.
            foreach (var box in reset)
            {
                if (layout) box.ResetDerivedStyle();
                else InteractionRestyler.InvalidatePaintCaches(box);
            }

            // The styles the cascade just made hold the values it was given back, so they are the ones the clock left behind.
            if (inherited is not null)
            {
                foreach (var state in _states)
                {
                    if (reset.Contains(state.Box)) state.StyleAfterWrite = state.Box.ComputedStyle;
                }
            }

            void Collect(CssBox box)
            {
                reset.Add(box);
                foreach (var child in box.Boxes) Collect(child);
            }
        }

        private readonly List<BoxState> _changed = [];
        private readonly List<Rect> _pageRects = [];

        // What a changed box covers, on each page it is split across: where it was drawn before this frame and where it is now, with what it paints around
        // itself. Worked out from the box's own matrix and its subtree's extent, which stay the same while only transform and opacity change.
        private void AddDirtyRegions(List<DirtyRegion> dirty)
        {
            var perPage = new Dictionary<int, List<Rect>>();

            var anyStale = false;
            foreach (var changedState in _changed) anyStale |= changedState.BleedStale;
            if (anyStale && _extentsFor is { } current)
            {
                _extentsStale = true;
                EnsureExtents(current, resetMatrices: false);
            }

            foreach (var state in _changed)
            {
                var after = state.Box.HasTransform ? state.Box.ActualTransformMatrix : Matrix3x2.Identity;
                var afterHas = state.Box.HasTransform;

                state.BleedStale = false;

                foreach (var part in state.Fragments)
                {
                    var before = part.Rebased(state.LastMatrix, state.LastHasTransform);
                    var now = part.Rebased(after, afterHas);
                    var layout = Union(Bounds(before, part.Extent), Bounds(now, part.Extent));
                    var shifted = _container.View.DisplacementOf(state.Box) is var (dx, dy) && (dx != 0 || dy != 0)
                        ? new Rect(layout.X + dx, layout.Y + dy, layout.Width, layout.Height)
                        : layout;

                    var rect = new Rect(part.Frame.X(shifted.X) - Inflate, part.Frame.Y(shifted.Y) - Inflate,
                        (shifted.Width / part.Frame.Ppp) + (Inflate * 2), (shifted.Height / part.Frame.Ppp) + (Inflate * 2));

                    if (!perPage.TryGetValue(part.Page, out var list)) perPage[part.Page] = list = [];
                    list.Add(rect);
                }

                state.LastMatrix = after;
                state.LastHasTransform = afterHas;
            }

            foreach (var (page, rects) in perPage)
            {
                foreach (var rect in Merge(rects))
                    dirty.Add(new DirtyRegion(page, rect));
            }
        }

        private const double Inflate = 2;

        /// <summary>
        /// What looks different after a layout pass: the places where a box, a line or a word is not where it was (each with what it paints around itself), and
        /// the strip a page gained when it grew, on each page, merged. Null when the pages cannot be compared box for box (their number or width changed), in
        /// which case the caller repaints them whole.
        /// </summary>
        internal static List<DirtyRegion>? DirtyRegionsForLayout(HtmlContainerInt container, IReadOnlyList<FragmentainerFragment> before, IReadOnlyList<FragmentainerFragment> after)
        {
            if (before.Count != after.Count) return null;

            var ppp = container.RenderContext.LayoutUnitsPerPoint;
            var result = new List<DirtyRegion>();
            for (var page = 0; page < after.Count; page++)
            {
                var oldGeometry = before[page].Geometry;
                var newGeometry = after[page].Geometry;
                if (Math.Abs(oldGeometry.SheetWidthPt - newGeometry.SheetWidthPt) > 0.01) return null;

                var oldFrame = new TextRunCollector.Frame(oldGeometry.ContentLeftPt, oldGeometry.ContentTopPt, container.MarginLeft, container.MarginTop, ppp);
                var newFrame = new TextRunCollector.Frame(newGeometry.ContentLeftPt, newGeometry.ContentTopPt, container.MarginLeft, container.MarginTop, ppp);

                var old = new Dictionary<(CssBox, int), (Rect Rect, int Signature)>();
                var counts = new Dictionary<CssBox, int>(ReferenceEqualityComparer.Instance);
                Snapshot(before[page].Root, oldFrame, old, counts);

                var rects = new List<Rect>();
                counts.Clear();
                Compare(after[page].Root, newFrame, old, counts, rects);

                // What is left of the old page is a box that is gone from it.
                foreach (var (_, entry) in old)
                    rects.Add(entry.Rect);

                var heightBefore = oldGeometry.SheetHeightPt;
                var heightAfter = newGeometry.SheetHeightPt;
                if (Math.Abs(heightBefore - heightAfter) > 0.01)
                {
                    var top = Math.Min(heightBefore, heightAfter);
                    rects.Add(new Rect(0, top, newGeometry.SheetWidthPt, Math.Max(heightBefore, heightAfter) - top));
                }

                foreach (var rect in Merge(rects))
                    result.Add(new DirtyRegion(page, rect));
            }

            return result;

            static void Snapshot(BoxFragment fragment, TextRunCollector.Frame frame, Dictionary<(CssBox, int), (Rect, int)> into, Dictionary<CssBox, int> counts)
            {
                counts[fragment.Box] = counts.TryGetValue(fragment.Box, out var n) ? n + 1 : 0;
                into[(fragment.Box, counts[fragment.Box])] = (Painted(fragment, frame), SignatureOf(fragment));
                foreach (var child in fragment.Children) Snapshot(child, frame, into, counts);
            }

            static void Compare(BoxFragment fragment, TextRunCollector.Frame frame, Dictionary<(CssBox, int), (Rect Rect, int Signature)> old, Dictionary<CssBox, int> counts, List<Rect> rects)
            {
                counts[fragment.Box] = counts.TryGetValue(fragment.Box, out var n) ? n + 1 : 0;
                var key = (fragment.Box, counts[fragment.Box]);
                var now = Painted(fragment, frame);
                if (old.Remove(key, out var was))
                {
                    if (was.Signature != SignatureOf(fragment)) rects.Add(Union(was.Rect, now));
                }
                else
                {
                    rects.Add(now);
                }

                foreach (var child in fragment.Children) Compare(child, frame, old, counts, rects);
            }

            // Where the fragment's box paints, on the page in points: its rectangle with what it draws around it, moved by its own transform.
            static Rect Painted(BoxFragment fragment, TextRunCollector.Frame frame)
            {
                var box = fragment.Box;
                var local = fragment.WholeBoxRect;
                var bleed = Math.Max(FragmentPainter.ShadowBleed(box), Math.Max(FragmentPainter.OutlineBleed(box), FragmentPainter.TextShadowBleed(box)));
                var layout = new Rect(local.X - bleed, local.Y - bleed, local.Width + (bleed * 2), local.Height + (bleed * 2));
                if (box.HasTransform) layout = Bounds(box.ActualTransformMatrix.RebaseOrigin(local.X, local.Y), layout);

                return new Rect(frame.X(layout.X) - Inflate, frame.Y(layout.Y) - Inflate, (layout.Width / frame.Ppp) + (Inflate * 2), (layout.Height / frame.Ppp) + (Inflate * 2));
            }

            // Anything that is painted in a different place changes this: the box's rectangle, each of its decoration rectangles and each of its words.
            static int SignatureOf(BoxFragment fragment)
            {
                var hash = new HashCode();
                hash.Add(fragment.WholeBoxRect);
                foreach (var line in fragment.Lines) hash.Add(line.Rect);
                foreach (var word in fragment.Words) hash.Add(word.Rect);
                return hash.ToHashCode();
            }
        }

        private static Rect Bounds(Matrix3x2 matrix, Rect rect)
        {
            var a = Vector2.Transform(new Vector2((float)rect.Left, (float)rect.Top), matrix);
            var b = Vector2.Transform(new Vector2((float)rect.Right, (float)rect.Top), matrix);
            var c = Vector2.Transform(new Vector2((float)rect.Right, (float)rect.Bottom), matrix);
            var d = Vector2.Transform(new Vector2((float)rect.Left, (float)rect.Bottom), matrix);
            var left = Math.Min(Math.Min(a.X, b.X), Math.Min(c.X, d.X));
            var top = Math.Min(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y));
            var right = Math.Max(Math.Max(a.X, b.X), Math.Max(c.X, d.X));
            var bottom = Math.Max(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y));
            return new Rect(left, top, right - left, bottom - top);
        }

        private static Rect Union(Rect a, Rect b)
        {
            var left = Math.Min(a.Left, b.Left);
            var top = Math.Min(a.Top, b.Top);
            return new Rect(left, top, Math.Max(a.Right, b.Right) - left, Math.Max(a.Bottom, b.Bottom) - top);
        }

        // Rectangles that touch or overlap are one region.
        private static List<Rect> Merge(List<Rect> rects)
        {
            var merged = new List<Rect>(rects.Count);
            foreach (var rect in rects)
            {
                var current = rect;
                for (var i = merged.Count - 1; i >= 0; i--)
                {
                    if (merged[i].Right >= current.Left && merged[i].Left <= current.Right && merged[i].Bottom >= current.Top && merged[i].Top <= current.Bottom)
                    {
                        current = Union(merged[i], current);
                        merged.RemoveAt(i);
                    }
                }

                merged.Add(current);
            }

            return merged;
        }

        private static bool IsCompositorProperty(string property) =>
            InteractionRestyler.IsTransformProperty(property) || property.Equals("opacity", StringComparison.OrdinalIgnoreCase);

        // Where each animated box is on its pages and what its subtree covers, found once for each layout (the fragment tree is rebuilt by a layout).
        private IReadOnlyList<FragmentainerFragment>? _extentsFor;

        private void EnsureExtents(IReadOnlyList<FragmentainerFragment> fragmentainers, bool resetMatrices = true)
        {
            if (ReferenceEquals(_extentsFor, fragmentainers) && !_extentsStale) return;

            _extentsFor = fragmentainers;
            _extentsStale = false;

            var wanted = new Dictionary<CssBox, BoxState>(_byBox.Count);
            foreach (var state in _states) wanted[state.Box] = state;
            foreach (var state in _states) state.Fragments.Clear();

            var ppp = _container.RenderContext.LayoutUnitsPerPoint;
            for (var page = 0; page < fragmentainers.Count; page++)
            {
                var geometry = fragmentainers[page].Geometry;
                var frame = new TextRunCollector.Frame(geometry.ContentLeftPt, geometry.ContentTopPt, _container.MarginLeft, _container.MarginTop, ppp);
                Collect(fragmentainers[page].Root, page, frame, wanted);
            }

            if (!resetMatrices) return;

            foreach (var state in _states)
            {
                state.LastHasTransform = state.Box.HasTransform;
                state.LastMatrix = state.LastHasTransform ? state.Box.ActualTransformMatrix : Matrix3x2.Identity;
            }
        }

        private bool _extentsStale;

        private void Collect(BoxFragment fragment, int page, TextRunCollector.Frame frame, Dictionary<CssBox, BoxState> wanted)
        {
            if (wanted.TryGetValue(fragment.Box, out var state))
            {
                var extent = FragmentPainter.SubtreeExtent(fragment) ?? fragment.WholeBoxRect;
                var bleed = FragmentPainter.SubtreeBleed(fragment);
                state.Fragments.Add(new FragmentPart(page, frame, new Rect(extent.X - bleed, extent.Y - bleed, extent.Width + (bleed * 2), extent.Height + (bleed * 2)),
                    fragment.WholeBoxRect.X, fragment.WholeBoxRect.Y));
            }

            foreach (var child in fragment.Children)
                Collect(child, page, frame, wanted);
        }

        private readonly record struct FragmentPart(int Page, TextRunCollector.Frame Frame, Rect Extent, double OriginX, double OriginY)
        {
            // The box's matrix is cached treating its own top-left as the origin; here it pivots at that point on the page.
            public Matrix3x2 Rebased(Matrix3x2 matrix, bool has) => has ? matrix.RebaseOrigin(OriginX, OriginY) : Matrix3x2.Identity;
        }

        private static IEnumerable<string> PropertiesOf(BoxState state)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var run in state.Transitions)
            {
                if (seen.Add(run.Property)) yield return run.Property;
            }

            foreach (var track in state.Tracks)
            {
                foreach (var property in track.Properties)
                {
                    if (seen.Add(property)) yield return property;
                }
            }
        }

        private string SampleTransition(TransitionRun run)
        {
            var local = run.Elapsed - run.DelaySeconds;
            if (local <= 0)
            {
                run.LastEased = 0;
                return run.From;
            }

            if (run.DurationSeconds <= 0 || local >= run.DurationSeconds)
            {
                run.LastEased = 1;
                return run.To;
            }

            var eased = run.Easing.Evaluate(local / run.DurationSeconds);
            run.LastEased = eased;
            if (eased == 0) return run.From;
            if (eased == 1) return run.To;
            return AnimationApplier.MixTransition(_parser, run.Box, run.Property, run.From, run.To, eased);
        }

        private static bool IsStackingProperty(string property) =>
            property.Equals("opacity", StringComparison.OrdinalIgnoreCase) || InteractionRestyler.IsTransformProperty(property)
            || property.Equals("filter", StringComparison.OrdinalIgnoreCase);

        private static bool IsNeutral(string property, string? value) =>
            string.IsNullOrWhiteSpace(value) || value.Trim().Equals("none", StringComparison.OrdinalIgnoreCase)
            || (property.Equals("opacity", StringComparison.OrdinalIgnoreCase) && value.Trim() is "1" or "1.0");

        // Follows the box tree: a document loaded again has new boxes, and one restyled in place has boxes whose styles were rebuilt.
        private void Bind()
        {
            if (_container.Root is not { } root) return;
            if (ReferenceEquals(root, _root) && _generation == _container.StyleGeneration)
            {
                // Nothing restyled; an image that has arrived since is the one thing to pick up.
                if (_imageGeneration != _container.ImageGeneration)
                {
                    CollectImages(root);
                    _contentGeneration++;
                }
                return;
            }

            var rebuilt = !ReferenceEquals(root, _root);

            if (rebuilt)
            {
                // Remember where each animation was so a reload does not restart it.
                _carried.Clear();
                foreach (var state in _states)
                {
                    if (state.Ordinal < 0) continue;
                    foreach (var track in state.Tracks)
                        _carried[(state.Ordinal, track.Name, track.Index)] = (track, state.Box);
                }

                foreach (var state in _states) state.Entry.Discard();
                _states.Clear();
                _byBox.Clear();
                _root = root;
                _images.Clear();
                CollectBoxes(root, rebuilt: true);

                // What the new tree no longer names was taken away by whatever caused the load.
                foreach (var (track, box) in _carried.Values) RetireTrack(track, box);
                _carried.Clear();
            }
            else
            {
                var existing = new List<BoxState>(_states);
                _states.Clear();
                _byBox.Clear();
                foreach (var state in existing) Rebind(state);
                // A box that gained an animation through the restyle (a hover rule that starts one) is found by walking again.
                CollectBoxes(root, rebuilt: false, existing);
            }

            _generation = _container.StyleGeneration;
            _contentGeneration++;
            foreach (var state in _states) state.Properties = [.. PropertiesOf(state)];
            _extentsStale = true;
            DropOrphanedTransitions();
            SelectPromoted();
            CollectImages(root);
        }

        // A transition whose element is gone (the document was loaded again without it) would keep the clock running for ever.
        private void DropOrphanedTransitions()
        {
            if (_transitions.Count == 0) return;
            List<(int, string)>? orphaned = null;
            foreach (var (key, run) in _transitions)
            {
                if (!(_byBox.TryGetValue(run.Box, out var state) && state.Transitions.Contains(run))) (orphaned ??= []).Add(key);
            }

            if (orphaned is null) return;
            foreach (var key in orphaned)
            {
                RetireRun(_transitions[key]);
                _transitions.Remove(key);
            }
        }

        private void Rebind(BoxState state)
        {
            // A style the clock did not leave behind was rebuilt by the cascade: its values are the ones the box has without the animation.
            var touched = !ReferenceEquals(state.Box.ComputedStyle, state.StyleAfterWrite);
            var previous = state.Tracks;
            var tracks = AnimationApplier.BuildLiveTracks(_parser, state.Box);
            CarryElapsed(state.Box, previous, tracks);
            state.Tracks = tracks;

            if (touched)
            {
                state.Base.Clear();
                state.Written.Clear();
                CaptureBase(state);
            }
            else
            {
                CaptureBase(state, onlyMissing: true);
            }

            if (state.Tracks.Count > 0 || state.Transitions.Count > 0 || HasTransition(state.Box)) Register(state);
        }

        private void CollectBoxes(CssBox box, bool rebuilt, List<BoxState>? existing = null)
        {
            if (_byBox.ContainsKey(box)) goto children;

            if (!AnimationApplier.IsNone(box.AnimationName) || HasTransition(box))
            {
                var ordinal = box.HtmlTag?.SourceOrdinal ?? -1;
                var state = new BoxState { Box = box, Ordinal = ordinal };
                var tracks = AnimationApplier.BuildLiveTracks(_parser, box);

                if (rebuilt && ordinal >= 0)
                {
                    foreach (var track in tracks)
                    {
                        if (_carried.Remove((ordinal, track.Name, track.Index), out var carried)) CarryState(carried.Track, track, box);
                    }
                }

                state.Tracks = tracks;
                CaptureBase(state);

                // Transition runs that survived a reload belong to the same element.
                foreach (var (key, run) in _transitions)
                {
                    if (key.Ordinal == ordinal && ordinal >= 0)
                    {
                        run.Box = box;
                        state.Transitions.Add(run);
                    }
                }

                if (tracks.Count > 0 || state.Transitions.Count > 0) Register(state);
            }

        children:
            foreach (var child in box.Boxes)
                CollectBoxes(child, rebuilt, existing);
        }

        // Whether a transition that is running belongs to this box's element.
        private bool HasTransition(CssBox box)
        {
            if (_transitions.Count == 0) return false;
            var ordinal = box.HtmlTag?.SourceOrdinal ?? -1;
            if (ordinal < 0) return false;
            foreach (var key in _transitions.Keys)
            {
                if (key.Ordinal == ordinal) return true;
            }

            return false;
        }

        private void Register(BoxState state)
        {
            if (_byBox.TryAdd(state.Box, state)) _states.Add(state);

            // Transition runs bound to this element follow its (possibly new) box.
            foreach (var (key, run) in _transitions)
            {
                if (key.Ordinal == state.Ordinal && state.Ordinal >= 0 && !state.Transitions.Contains(run))
                {
                    run.Box = state.Box;
                    if (run.Handle is { } handle) handle.Element = new DocumentElement(state.Box);
                    state.Transitions.Add(run);
                }
            }
        }

        private void CarryElapsed(CssBox box, List<AnimationApplier.LiveTrack> from, List<AnimationApplier.LiveTrack> to)
        {
            foreach (var old in from)
            {
                AnimationApplier.LiveTrack? match = null;
                foreach (var track in to)
                {
                    if (old.Index == track.Index && old.Name == track.Name)
                    {
                        match = track;
                        break;
                    }
                }

                if (match is null) RetireTrack(old, box);
                else CarryState(old, match, box);
            }
        }

        // The animation goes on as the one it was: where it was, what was said of it, and the object the host holds for it.
        private static void CarryState(AnimationApplier.LiveTrack old, AnimationApplier.LiveTrack track, CssBox box)
        {
            track.Elapsed = old.Elapsed;
            track.Finished = old.Finished && track.IterationCount == old.IterationCount;
            track.Started = old.Started;
            track.Iteration = old.Iteration;
            track.Ended = old.Ended && track.IterationCount == old.IterationCount;
            track.ControlPaused = old.ControlPaused;
            track.PlaybackRate = old.PlaybackRate;
            track.Cancelled = old.Cancelled;

            if (old.Handle is { } handle)
            {
                track.Handle = handle;
                handle.Source = track;
                handle.Element = new DocumentElement(box);
            }
        }

        // The element stopped naming the animation: one that had begun and not finished is cancelled.
        private void RetireTrack(AnimationApplier.LiveTrack track, CssBox box)
        {
            if (track.Removed) return;

            track.Removed = true;
            if (track.Started && !track.Ended && !track.Cancelled)
                Emit(AnimationEventKind.AnimationCancel, box, track.Name, Math.Clamp(track.Elapsed - track.DelaySeconds, 0, track.ActiveSeconds));

            track.Handle?.Complete(cancelled: !track.Ended);
        }

        #region Control (DocumentAnimation)

        /// <summary>The animations and transitions that are running on the document, or on the element with <paramref name="ordinal"/>, as objects to look at and control.</summary>
        internal List<DocumentAnimation> GetAnimations(int? ordinal)
        {
            Bind();

            var result = new List<DocumentAnimation>();
            foreach (var state in _states)
            {
                if (ordinal is { } wanted && state.Ordinal != wanted) continue;

                foreach (var track in state.Tracks)
                {
                    if (track.Removed || track.Cancelled) continue;
                    result.Add(track.Handle ??= new DocumentAnimation(this, track, state.Box));
                }

                foreach (var run in state.Transitions)
                {
                    if (run.Removed || run.Cancelled || run.Finished) continue;
                    result.Add(run.Handle ??= new DocumentAnimation(this, run, state.Box));
                }
            }

            return result;
        }

        /// <summary>Asks for a step: something about an animation changed that the next one has to show, though nothing may be running.</summary>
        internal void Wake() => IsRunning = true;

        // The flags that say what has been reported follow the position, without reporting: a seek is not a run through the animation.
        internal void Seek(AnimationApplier.LiveTrack track, double seconds)
        {
            track.Elapsed = seconds;
            track.Finished = false;

            var active = track.ActiveSeconds;
            track.Started = seconds >= track.DelaySeconds;
            track.Ended = !double.IsInfinity(active) && seconds - track.DelaySeconds >= active;
            track.Iteration = track.DurationSeconds > 0 && seconds >= track.DelaySeconds
                ? (int)Math.Min(Math.Min(Math.Floor((seconds - track.DelaySeconds) / track.DurationSeconds), double.IsInfinity(active) ? int.MaxValue : Math.Ceiling(active / track.DurationSeconds) - 1), int.MaxValue - 1)
                : 0;
            Wake();
        }

        internal void Seek(TransitionRun run, double seconds)
        {
            run.Elapsed = seconds;
            run.Started = seconds >= run.DelaySeconds;
            Wake();
        }

        internal void Play(DocumentAnimation animation)
        {
            if (animation.Source is AnimationApplier.LiveTrack track)
            {
                if (track.Removed) return;

                track.ControlPaused = false;
                var active = track.ActiveSeconds;
                if (track.Cancelled || (track.Finished && track.PlaybackRate >= 0) || (track.PlaybackRate < 0 && track.Elapsed <= 0 && !double.IsInfinity(active)))
                {
                    track.Cancelled = false;
                    Seek(track, track.PlaybackRate >= 0 ? 0 : track.DelaySeconds + active);
                    track.Started = track.PlaybackRate < 0;
                    track.Ended = false;
                    track.Iteration = 0;
                }
            }
            else if (animation.Source is TransitionRun run && !run.Removed)
            {
                run.ControlPaused = false;
            }

            Wake();
        }

        internal void Pause(DocumentAnimation animation)
        {
            if (animation.Source is AnimationApplier.LiveTrack track) track.ControlPaused = true;
            else if (animation.Source is TransitionRun run) run.ControlPaused = true;
        }

        internal void Finish(DocumentAnimation animation)
        {
            if (animation.Source is AnimationApplier.LiveTrack track)
            {
                if (track.Removed || track.Cancelled) return;
                if (double.IsInfinity(track.ActiveSeconds) && track.PlaybackRate >= 0)
                    throw new InvalidOperationException("An animation that repeats for ever has no end to finish at.");

                // Where it ends up, with what led there counted as reported but the end itself, which the next step reports.
                track.ControlPaused = false;
                if (track.PlaybackRate >= 0)
                {
                    track.Started = true;
                    track.Iteration = track.DurationSeconds > 0 ? (int)Math.Max(0, Math.Ceiling(track.ActiveSeconds / track.DurationSeconds) - 1) : 0;
                    track.Elapsed = track.DelaySeconds + track.ActiveSeconds;
                }
                else
                {
                    track.Elapsed = 0;
                }
            }
            else if (animation.Source is TransitionRun run && !run.Removed && !run.Finished)
            {
                run.ControlPaused = false;
                run.Elapsed = run.PlaybackRate >= 0 ? run.DelaySeconds + run.DurationSeconds : 0;
            }

            Wake();
        }

        internal void Cancel(DocumentAnimation animation)
        {
            if (animation.Source is AnimationApplier.LiveTrack track)
            {
                if (track.Removed || track.Cancelled) return;

                track.Cancelled = true;
                if (track.Started && !track.Ended)
                    Emit(AnimationEventKind.AnimationCancel, animation.Element.Box, track.Name, Math.Clamp(track.Elapsed - track.DelaySeconds, 0, track.ActiveSeconds));

                animation.Complete(cancelled: true);
            }
            else if (animation.Source is TransitionRun run && !run.Removed && !run.Finished)
            {
                // It runs out on the next step, writing the value the style has, and reports a cancel in place of an end.
                run.Cancelled = true;
                run.ControlPaused = false;
                run.Elapsed = run.DelaySeconds + run.DurationSeconds;
            }

            Wake();
        }

        internal void Reverse(DocumentAnimation animation)
        {
            if (animation.Source is AnimationApplier.LiveTrack track)
            {
                if (track.Removed || track.Cancelled) return;

                track.PlaybackRate = -track.PlaybackRate;
                var active = track.ActiveSeconds;
                if (!double.IsInfinity(active)) track.Elapsed = Math.Min(track.Elapsed, track.DelaySeconds + active);
                track.Finished = false;
                track.Ended = false;
            }
            else if (animation.Source is TransitionRun run && !run.Removed)
            {
                run.PlaybackRate = -run.PlaybackRate;
            }

            Wake();
        }

        #endregion

        private void CaptureBase(BoxState state, bool onlyMissing = false)
        {
            foreach (var property in PropertiesOf(state))
            {
                if (onlyMissing && state.Base.ContainsKey(property)) continue;
                state.Base[property] = ReadProperty(state.Box, property);
            }
        }

        // A box is promoted when it animates transform or opacity and nothing inside it does: what it paints is then the same every frame, and a
        // frame only has to place it again.
        private void SelectPromoted()
        {
            var animatedInside = new HashSet<CssBox>();
            foreach (var state in _states)
            {
                state.Compositor = false;
                foreach (var track in state.Tracks)
                {
                    foreach (var property in track.Properties)
                    {
                        if (InteractionRestyler.IsTransformProperty(property) || property.Equals("opacity", StringComparison.OrdinalIgnoreCase)) state.Compositor = true;
                    }
                }

                foreach (var run in state.Transitions)
                {
                    if (InteractionRestyler.IsTransformProperty(run.Property) || run.Property.Equals("opacity", StringComparison.OrdinalIgnoreCase)) state.Compositor = true;
                }

                if (!state.Compositor) continue;
                for (var ancestor = state.Box.ParentBox; ancestor is not null; ancestor = ancestor.ParentBox) animatedInside.Add(ancestor);
            }

            foreach (var state in _states)
            {
                var promote = state.Compositor && !animatedInside.Contains(state.Box);
                if (state.Promoted && !promote) state.Entry.Discard();
                state.Promoted = promote;
            }
        }

        private void CollectImages(CssBox root)
        {
            _imageGeneration = _container.ImageGeneration;
            _images.Clear();
            void Walk(CssBox box)
            {
                if (box is CssBoxImage { Image: { FrameCount: > 1 } image } && !_images.Contains(image)) _images.Add(image);
                foreach (var child in box.Boxes) Walk(child);
            }

            Walk(root);
        }

        // Plays each animated image: the frame its clock says, shared by every box that shows it.
        private bool AdvanceImages(double dt, IReadOnlyList<FragmentainerFragment> fragmentainers, List<DirtyRegion> dirty)
        {
            if (_images.Count == 0) return false;

            var running = false;
            HashSet<Image>? moved = null;
            foreach (var image in _images)
            {
                var elapsed = (_imageElapsed.TryGetValue(image, out var e) ? e : 0) + dt;
                _imageElapsed[image] = elapsed;

                var (frame, finished) = FrameAt(image, elapsed);
                if (!finished) running = true;

                if (frame != image.CurrentFrame)
                {
                    image.CurrentFrame = frame;
                    (moved ??= new HashSet<Image>(ReferenceEqualityComparer.Instance)).Add(image);
                }
            }

            if (moved is null || _container.Root is not { } root) return running;
            _contentGeneration++;

            var boxes = new HashSet<CssBox>();
            void Walk(CssBox box)
            {
                if (box is CssBoxImage { Image: { } image } && moved.Contains(image)) boxes.Add(box);
                foreach (var child in box.Boxes) Walk(child);
            }

            Walk(root);
            foreach (var box in boxes)
                dirty.AddRange(StyleUpdater.DirtyRegionsOf(_container, box, fragmentainers));

            return running;
        }

        private static (int Frame, bool Finished) FrameAt(Image image, double elapsedSeconds)
        {
            var count = image.FrameCount;
            var cycle = 0.0;
            for (var i = 0; i < count; i++) cycle += Math.Max(image.GetFrameDuration(i).TotalSeconds, 0.01);
            if (!(cycle > 0)) return (0, true);

            var loops = image.LoopCount;
            if (loops > 0 && elapsedSeconds >= cycle * loops) return (count - 1, true);

            var t = elapsedSeconds % cycle;
            for (var i = 0; i < count; i++)
            {
                var d = Math.Max(image.GetFrameDuration(i).TotalSeconds, 0.01);
                if (t < d) return (i, false);
                t -= d;
            }

            return (count - 1, false);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var state in _states) state.Entry.Discard();
            _states.Clear();
            _byBox.Clear();
        }
    }
}
