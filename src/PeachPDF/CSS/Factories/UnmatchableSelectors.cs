using System;
using System.Collections.Frozen;
using System.Collections.Generic;

namespace PeachPDF.CSS
{
    /// <summary>
    /// The selectors PeachPDF <em>recognizes</em> but does not match: there is no shadow tree, no form
    /// control state, no interaction, no media playback and no browsing history in a static PDF, so a
    /// selector naming one of those has nothing to select.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Registering them matters because an unknown pseudo-class or pseudo-element invalidates the
    /// <em>whole</em> selector list it appears in (CSS Selectors 3 §4), taking the parts PeachPDF does
    /// support down with it. Tailwind CSS v4 emits its entire design-token block as
    /// <c>:root, :host { … }</c>, so one unrecognized <c>:host</c> discarded every <c>--color-*</c>,
    /// <c>--font-*</c> and <c>--spacing</c> token in the document. Parsing the name and never matching it
    /// — the treatment <c>:visited</c>/<c>:active</c> already get in <c>CssData.DoesSelectorMatch</c> —
    /// keeps the list valid so its <c>:root</c> half applies, while <c>:host</c> correctly selects
    /// nothing.
    /// </para>
    /// <para>
    /// The sets below are the selectors both Blink and Gecko implement; a vendor-prefixed name is
    /// accepted wholesale by <see cref="IsVendorExtension"/> instead of being enumerated, since a
    /// leading hyphen marks a vendor/private extension (CSS Syntax 3 §2) and therefore names something
    /// PeachPDF has no elements or state for by definition. This is deliberately <em>not</em> a
    /// blanket "accept every unknown name": a genuine typo still invalidates the rule, as a browser's
    /// would.
    /// </para>
    /// </remarks>
    internal static class UnmatchableSelectors
    {
        /// <summary>
        /// Ident-form pseudo-classes that parse and never match. Includes the state-based ones PeachPDF
        /// has always accepted (<c>:hover</c>, <c>:visited</c>, …) so that "parses, selects nothing" has
        /// exactly one list rather than being split between this type and the factory that reads it.
        /// </summary>
        public static readonly FrozenSet<string> PseudoClasses =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                // Selectors 3/4 — no interaction, focus, or browsing history in a static PDF.
                PseudoClassNames.Visited,
                PseudoClassNames.Active,
                PseudoClassNames.Hover,
                PseudoClassNames.Focus,
                PseudoClassNames.FocusVisible,
                PseudoClassNames.FocusWithin,
                PseudoClassNames.Target,
                PseudoClassNames.TargetWithin,
                PseudoClassNames.LocalLink,
                PseudoClassNames.Current,
                PseudoClassNames.Past,
                PseudoClassNames.Future,

                // HTML form/element state — no form controls are ever interacted with.
                PseudoClassNames.Enabled,
                PseudoClassNames.Disabled,
                PseudoClassNames.Default,
                PseudoClassNames.Checked,
                PseudoClassNames.Unchecked,
                PseudoClassNames.Indeterminate,
                PseudoClassNames.PlaceholderShown,
                PseudoClassNames.Valid,
                PseudoClassNames.Invalid,
                PseudoClassNames.UserValid,
                PseudoClassNames.UserInvalid,
                PseudoClassNames.Required,
                PseudoClassNames.Optional,
                PseudoClassNames.ReadOnly,
                PseudoClassNames.ReadWrite,
                PseudoClassNames.InRange,
                PseudoClassNames.OutOfRange,
                PseudoClassNames.Autofill,
                PseudoClassNames.Open,
                PseudoClassNames.PopoverOpen,
                PseudoClassNames.Modal,

                // Shadow DOM — PeachPDF builds no shadow trees. `:host` is the one Tailwind v4 emits.
                PseudoClassNames.Host,
                PseudoClassNames.Shadow,
                PseudoClassNames.Defined,

                // Fullscreen / Picture-in-Picture — no viewport to go fullscreen in.
                PseudoClassNames.Fullscreen,
                PseudoClassNames.PictureInPicture,

                // Media playback state — a PDF plays nothing.
                PseudoClassNames.Playing,
                PseudoClassNames.Paused,
                PseudoClassNames.Seeking,
                PseudoClassNames.Buffering,
                PseudoClassNames.Stalled,
                PseudoClassNames.Muted,
                PseudoClassNames.VolumeLocked
            }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Functional pseudo-classes that parse and never match. Their argument is kept verbatim in the
        /// selector's text (so serialization round-trips) but never interpreted. <c>:lang()</c> used to be
        /// here too, but is not routed through this list any more - it's registered in
        /// <c>SelectorConstructor.PseudoClassFunctions</c>, which always intercepts it first, so this set
        /// never actually gated its "never matches" behavior even before it gained real matching
        /// (<c>LangSelector</c>/<c>CssData.DoesSelectorMatch(LangSelector, ...)</c>).
        /// </summary>
        public static readonly FrozenSet<string> FunctionalPseudoClasses =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                PseudoClassNames.Host,
                PseudoClassNames.HostContext,
                PseudoClassNames.State,
                PseudoClassNames.Dir
            }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Ident-form pseudo-elements that parse and never match — none of them names a box PeachPDF
        /// generates. <c>::placeholder</c> is the one Tailwind v4's preflight emits unconditionally.
        /// </summary>
        public static readonly FrozenSet<string> PseudoElements =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                PseudoElementNames.Placeholder,
                PseudoElementNames.Backdrop,
                PseudoElementNames.FileSelectorButton,
                PseudoElementNames.DetailsContent,
                PseudoElementNames.TargetText,
                PseudoElementNames.SpellingError,
                PseudoElementNames.GrammarError,
                PseudoElementNames.Cue,
                PseudoElementNames.CueRegion,
                PseudoElementNames.ViewTransition
            }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Functional pseudo-elements that parse and never match, keeping their argument verbatim in the
        /// selector's text.
        /// </summary>
        public static readonly FrozenSet<string> FunctionalPseudoElements =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                PseudoElementNames.Part,
                PseudoElementNames.Slotted,
                PseudoElementNames.Highlight,
                PseudoElementNames.Cue,
                PseudoElementNames.CueRegion,
                PseudoElementNames.ViewTransitionGroup,
                PseudoElementNames.ViewTransitionImagePair,
                PseudoElementNames.ViewTransitionOld,
                PseudoElementNames.ViewTransitionNew
            }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Is <paramref name="name"/> a vendor/private extension? A leading hyphen is the CSS Syntax 3 §2
        /// vendor-prefix convention (<c>-webkit-</c>, <c>-moz-</c>, <c>-ms-</c>, <c>-o-</c>, …), so such a
        /// name can only ever refer to a UA-internal box or state that PeachPDF does not have. Accepting
        /// them is what keeps a reset stylesheet's <c>::-webkit-file-upload-button</c>,
        /// <c>::-moz-focus-inner</c> or <c>:-moz-focusring</c> rule from invalidating the list it shares
        /// with a standard selector.
        /// </summary>
        public static bool IsVendorExtension(string name) =>
            name.Length > 1 && name[0] == '-';

        /// <remarks>
        /// The ident-form sets have no matching predicate because their consumers - the two selector
        /// factories - fold them into their own name→selector dictionary rather than asking a question
        /// per lookup. Only the functional forms, which SelectorConstructor builds itself, need one.
        /// </remarks>
        public static bool IsFunctionalPseudoClass(string name) =>
            FunctionalPseudoClasses.Contains(name) || IsVendorExtension(name);

        public static bool IsFunctionalPseudoElement(string name) =>
            FunctionalPseudoElements.Contains(name) || IsVendorExtension(name);
    }
}
