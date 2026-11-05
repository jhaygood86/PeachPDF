using PeachBrackets.Css;
using PeachDrawing.Core;
using System;
using System.Collections.Generic;

namespace PeachBrackets.Html.Dom
{
    internal partial class CssBox
    {
        /// <summary>
        /// Whether a rule that reads the interaction state (<c>:hover</c>, <c>:active</c>, <c>:focus</c> and the like) was among the candidates
        /// the cascade offered this box, whether or not it matched: the boxes a change of that state can restyle (see <c>InteractionRestyler</c>).
        /// </summary>
        internal bool HasStatefulCandidates { get; private set; }

        /// <summary>
        /// Whether one of those rules reads state other than this box's own (a combinator, <c>:has()</c>, <c>:is()</c>...), so it can match
        /// differently when only another element's state changed. A box with only rules on its own state is looked at when its own state changes.
        /// </summary>
        internal bool HasRelationalStatefulCandidates { get; private set; }

        /// <summary>
        /// Whether a rule that reads the interaction state was among the candidates the cascade offered an element inside this inline SVG. Those elements are not
        /// boxes the incremental restyle can restyle, so it compares what they match under the two states (see <c>InteractionRestyler</c>).
        /// </summary>
        internal bool HasSvgInnerStatefulCandidates => SvgInnerStatefulSelectors is { Count: > 0 };

        /// <summary>The selectors of those rules, each once, for asking which of them a given changed element could make match differently.</summary>
        internal List<ISelector>? SvgInnerStatefulSelectors { get; private set; }

        internal void NoteSvgInnerStatefulCandidate(ISelector selector)
        {
            SvgInnerStatefulSelectors ??= [];
            foreach (var known in SvgInnerStatefulSelectors)
            {
                if (ReferenceEquals(known, selector)) return;
            }

            SvgInnerStatefulSelectors.Add(selector);
        }

        internal void NoteStatefulCandidate(bool ownStateOnly)
        {
            HasStatefulCandidates = true;
            if (!ownStateOnly) HasRelationalStatefulCandidates = true;
        }

        internal void CopyStatefulCandidatesFrom(bool any, bool relational)
        {
            HasStatefulCandidates |= any;
            HasRelationalStatefulCandidates |= relational;
        }

        /// <summary>
        /// What <c>user-select</c> comes to for this box (CSS Basic User Interface 4 section 6.2): a declared value is itself (<c>contain</c> is
        /// <c>text</c> for a reader that has no editing host), and <c>auto</c> is <c>none</c> or <c>all</c> when the parent's is, and <c>text</c>
        /// otherwise.
        /// </summary>
        public UserSelect EffectiveUserSelect
        {
            get
            {
                var declared = UserSelect.Value;
                if (declared == Css.UserSelect.Contain)
                    return Css.UserSelect.Text;

                if (declared != Css.UserSelect.Auto)
                    return declared;

                var parent = ParentBox?.EffectiveUserSelect ?? Css.UserSelect.Text;
                return parent is Css.UserSelect.None or Css.UserSelect.All ? parent : Css.UserSelect.Text;
            }
        }

        /// <summary>
        /// Whether this box takes no part in user interaction: <c>interactivity: inert</c> (inherited) or the HTML <c>inert</c> attribute on it
        /// or an ancestor. An inert box is skipped by hit testing, is not a link, cannot hold a selection or focus, and shows the default pointer.
        /// </summary>
        public bool IsInert
        {
            get
            {
                if (Interactivity.Value == Css.Interactivity.Inert)
                    return true;

                for (var box = this; box is not null; box = box.ParentBox)
                {
                    if (box.HtmlTag is { } tag && tag.HasAttribute("inert"))
                        return true;
                }

                return false;
            }
        }

        /// <summary>
        /// The gestures a touch may start on this box: its <c>touch-action</c> intersected with that of every ancestor (CSS Pointer Events,
        /// "touch-action"), since the document is the only scroller.
        /// </summary>
        public TouchActions EffectiveTouchActions
        {
            get
            {
                var allowed = TouchActions.All;
                for (var box = this; box is not null; box = box.ParentBox)
                    allowed &= ParseTouchAction(box.TouchAction);

                return allowed;
            }
        }

        private static TouchActions ParseTouchAction(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return TouchActions.All;

            var pans = TouchActions.None;
            var pinch = false;
            var xGroup = false;
            var yGroup = false;
            foreach (var word in value.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
            {
                switch (word.ToLowerInvariant())
                {
                    case "auto": return TouchActions.All;
                    case "none": return TouchActions.None;
                    case "manipulation": return TouchActions.Pan | TouchActions.PinchZoom;
                    case "pan-x": pans |= TouchActions.PanLeft | TouchActions.PanRight; xGroup = true; break;
                    case "pan-left": pans |= TouchActions.PanLeft; xGroup = true; break;
                    case "pan-right": pans |= TouchActions.PanRight; xGroup = true; break;
                    case "pan-y": pans |= TouchActions.PanUp | TouchActions.PanDown; yGroup = true; break;
                    case "pan-up": pans |= TouchActions.PanUp; yGroup = true; break;
                    case "pan-down": pans |= TouchActions.PanDown; yGroup = true; break;
                    case "pinch-zoom": pinch = true; break;
                }
            }

            _ = xGroup;
            _ = yGroup;
            return pans | (pinch ? TouchActions.PinchZoom : TouchActions.None);
        }

        /// <summary>The colour of the text caret: <c>auto</c> is the text colour.</summary>
        public PaintColor ActualCaretColor =>
            IsOwnColor(CaretColor) ? ActualColor : GetActualColor(CaretColor);

        /// <summary>The accent colour for form controls, or <see langword="null"/> for <c>auto</c> (the built-in colours).</summary>
        public PaintColor? ActualAccentColor =>
            IsAutoColor(AccentColor) ? null : IsOwnColor(AccentColor) ? ActualColor : GetActualColor(AccentColor);

        // auto, and currentcolor, are the box's own text color (currentcolor on the box that declared it, which is not inherited as a keyword).
        private static bool IsOwnColor(string? value) =>
            IsAutoColor(value) || string.Equals(value!.Trim(), "currentcolor", StringComparison.OrdinalIgnoreCase);

        private static bool IsAutoColor(string? value) =>
            string.IsNullOrWhiteSpace(value) || string.Equals(value.Trim(), "auto", StringComparison.OrdinalIgnoreCase);
    }
}
