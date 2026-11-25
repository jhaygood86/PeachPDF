namespace PeachBrackets.Html.Dom
{
    /// <summary>The popover state a script sets on an element: whether the popover is showing, which <c>:popover-open</c> answers.</summary>
    internal partial class CssBox
    {
        /// <summary>Whether the element is a popover that is showing (set by the script host; a page that is not scripted has none open).</summary>
        internal bool PopoverOpen { get; set; }

        /// <summary>
        /// For a <c>script</c> element: whether it has run, or was made by the fragment parser (innerHTML and the like), which the HTML standard marks as
        /// already started so that it never runs.
        /// </summary>
        internal bool ScriptStarted { get; set; }
    }
}
