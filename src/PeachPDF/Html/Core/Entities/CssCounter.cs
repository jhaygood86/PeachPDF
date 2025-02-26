namespace PeachPDF.Html.Core.Entities
{
    internal record CssCounter(
        string Name,
        int Value,
        bool IsReversed,
        bool IsNewScope,
        CssCounter? ParentScope
    );
}
