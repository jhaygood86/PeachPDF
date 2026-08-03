namespace PeachPDF.CSS
{
    internal interface IContainerRule : IConditionRule
    {
        string Name { get; set; }
        MediaList Media { get; }

        /// <summary>
        /// The parsed <c>style(...)</c> condition tree (CSS Containment 3 SS7.3), when this rule's
        /// condition is a style query rather than a size-feature one - exactly one of this and
        /// <see cref="Media"/>'s own feature content is meaningful for a given rule (see
        /// <c>StylesheetComposer.CreateContainer</c>'s <c>style(</c> detection). <c>null</c> for an
        /// ordinary size-feature <c>@container</c> condition.
        /// </summary>
        IStyleQueryCondition StyleCondition { get; set; }
    }
}