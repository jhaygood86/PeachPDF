using System;

namespace PeachPDF.Html.Core.Animation
{
    /// <summary>The <c>animation-direction</c> keywords (CSS Animations 1 §3.7).</summary>
    internal enum AnimationDirectionKind
    {
        Normal,
        Reverse,
        Alternate,
        AlternateReverse
    }

    /// <summary>
    /// Maps a snapshot position onto one animation's keyframe progress. A PDF is a single moment, so instead of
    /// a clock there is one number, <see cref="PdfGenerateConfig.AnimationProgress"/>, and each animation is
    /// sampled at that fraction of <em>its own</em> run: the start of its active interval at 0, its end at 1.
    /// </summary>
    internal static class AnimationTimeline
    {
        /// <summary>
        /// The progress through the keyframes (0 = the <c>0%</c> keyframe, 1 = the <c>100%</c> keyframe), after
        /// iteration and direction are applied, or null when the animation contributes nothing.
        /// </summary>
        /// <param name="fraction">The snapshot position, 0 to 1.</param>
        /// <param name="durationSeconds"><c>animation-duration</c>.</param>
        /// <param name="iterationCount"><c>animation-iteration-count</c>; <see cref="double.PositiveInfinity"/> for <c>infinite</c>.</param>
        /// <param name="direction"><c>animation-direction</c>.</param>
        /// <param name="fillsForwards">Whether <c>animation-fill-mode</c> is <c>forwards</c> or <c>both</c>.</param>
        /// <remarks>
        /// <para>
        /// The <em>span</em> a fraction is taken of is the active duration (duration times iteration count) of a
        /// finite animation, and one iteration of an infinite one - it has no end, so "the end" means the end of
        /// its first cycle. <c>animation-delay</c> is deliberately not part of it: the delay positions an
        /// animation on a timeline this renderer does not have, and counting it would make a delayed animation
        /// render untouched at the start.
        /// </para>
        /// <para>
        /// The snapshot is taken inside the active interval, so <c>animation-fill-mode</c> and
        /// <c>animation-play-state</c> do not decide whether an animation applies. The one exception is an
        /// animation with no active interval at all (a zero duration or iteration count): it has no inside, and per
        /// CSS Animations 1 §3.9 only a <c>forwards</c> fill leaves an effect behind, in its final state.
        /// </para>
        /// </remarks>
        public static double? DirectedProgress(double fraction, double durationSeconds, double iterationCount,
            AnimationDirectionKind direction, bool fillsForwards)
        {
            fraction = Math.Clamp(fraction, 0, 1);

            if (!(durationSeconds > 0) || !(iterationCount > 0))
            {
                if (!fillsForwards) return null;

                // The animation ends the instant it starts; what is left is where its active interval ends.
                // With no iterations at all there is no progress to end on: overall progress is the iteration count, 0.
                var (finalProgress, finalIteration) = iterationCount > 0 ? EndOfActiveInterval(iterationCount) : (0d, 0d);
                return Direct(finalProgress, finalIteration, direction);
            }

            double iterationProgress, currentIteration;

            if (fraction >= 1)
            {
                (iterationProgress, currentIteration) = EndOfActiveInterval(iterationCount);
            }
            else
            {
                var span = double.IsPositiveInfinity(iterationCount) ? durationSeconds : durationSeconds * iterationCount;
                var overall = fraction * span / durationSeconds;
                currentIteration = Math.Floor(overall);
                iterationProgress = overall - currentIteration;
            }

            return Direct(iterationProgress, currentIteration, direction);
        }

        /// <summary>
        /// The iteration progress and the (zero-based) iteration at the end of the active interval. CSS Animations
        /// 1 §4.3 / Web Animations 1 §4.6.3: the progress is the fractional part of the count, or 1 for a whole
        /// number of cycles. An infinite animation has no end, so its end is the end of the first cycle.
        /// </summary>
        private static (double IterationProgress, double CurrentIteration) EndOfActiveInterval(double iterationCount)
        {
            if (double.IsPositiveInfinity(iterationCount)) return (1, 0);

            var remainder = iterationCount % 1;
            return remainder == 0 ? (1, iterationCount - 1) : (remainder, Math.Floor(iterationCount));
        }

        private static double Direct(double iterationProgress, double currentIteration, AnimationDirectionKind direction)
        {
            var odd = currentIteration % 2 == 1;
            var reversed = direction switch
            {
                AnimationDirectionKind.Reverse => true,
                AnimationDirectionKind.Alternate => odd,
                AnimationDirectionKind.AlternateReverse => !odd,
                _ => false
            };

            return reversed ? 1 - iterationProgress : iterationProgress;
        }
    }
}
