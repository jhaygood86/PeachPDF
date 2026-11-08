using System;
using PeachBrackets.Css;
using PeachBrackets.Html.Parse;
using PeachBrackets.Html.Utils;

namespace PeachBrackets.Html.Dom
{
    /// <summary><c>block-step-*</c> (CSS Rhythmic Sizing 1 §4): rounding a block container's height to a multiple of a step.</summary>
    internal partial class CssBox
    {
        /// <summary>Space <c>block-step-insert: margin-box</c> added before / after this box. Part of <see cref="ActualMarginTop"/>/<see cref="ActualMarginBottom"/>.</summary>
        private double _blockStepMarginTop;

        private double _blockStepMarginBottom;

        /// <summary>Whether the layout pass that completed this box continued it from an earlier fragmentainer.</summary>
        private bool _layoutWasResumed;

        private void ResetBlockStepMargins() => _blockStepMarginTop = _blockStepMarginBottom = 0;

        /// <summary>
        /// Rounds this block container's border-box height to a multiple of <c>block-step-size</c> and puts the difference where
        /// <c>block-step-insert</c> says: into the margin (the border box is unchanged and a margin is added) or into the padding/content
        /// (the box grows). <c>block-step-align</c> chooses how that space is shared before and after the box's content (or, for the margin,
        /// before and after the box); <c>auto</c> and <c>start</c> put it after, <c>end</c> before. A shrink (<c>block-step-round: down</c>/<c>nearest</c>) can only come out of the
        /// margin, which may go negative, never out of padding or content.
        /// </summary>
        private void ApplyBlockStep()
        {
            // A box that continues into a later fragmentainer, or arrives from an earlier one, has content already emitted
            // elsewhere that moving it now would leave behind: it is rounded as a whole box only.
            if (_layoutWasResumed || PendingBreakToken is not null) return;

            if (_computedStyle.BoxModel.BlockStepSize is not { Length: > 0 } sizeText
                || sizeText.Equals(Keywords.None, StringComparison.OrdinalIgnoreCase)
                || DerivedStyle.ActualDisplay is not (Keywords.Block or Keywords.FlowRoot or Keywords.ListItem)
                || IsFlexOrGridItem(this) || WritingModeFrame.For(this).IsVertical)
            {
                return;
            }

            var size = CssValueParser.ParseLength(sizeText, 0, this);
            var height = ActualBottom - Location.Y;
            if (size <= 0 || height <= 0) return;

            var steps = height / size;
            var count = (_computedStyle.BoxModel.BlockStepRound?.Trim().ToLowerInvariant()) switch
            {
                "down" => Math.Floor(steps + 1e-9),
                "nearest" => Math.Round(steps, MidpointRounding.AwayFromZero),
                _ => Math.Ceiling(steps - 1e-9)
            };

            var extra = Math.Max(count, 1) * size - height;
            if (Math.Abs(extra) < 1e-6) return;

            var align = _computedStyle.BoxModel.BlockStepAlign?.Trim().ToLowerInvariant() ?? Keywords.Auto;
            // start keeps the box at the start of the stepped space (the extra goes after it), end puts it at the end (the extra goes before),
            // and auto is start.
            var before = align switch { "end" => extra, "center" => extra / 2, _ => 0 };
            var after = extra - before;

            var insert = _computedStyle.BoxModel.BlockStepInsert?.Trim().ToLowerInvariant();
            if (insert is "padding-box" or "content-box")
            {
                if (extra < 0) return;

                // The box grows, and its content moves down by the share that goes above it.
                ActualBottom += extra;
                if (before > 0)
                {
                    foreach (var child in Boxes) child.OffsetTop(before);
                    OffsetOwnLineBoxesTop(before);
                }

                return;
            }

            _blockStepMarginTop = before;
            _blockStepMarginBottom = after;
            if (before != 0) OffsetTop(before);
        }
    }
}
