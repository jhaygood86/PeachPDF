namespace PeachPDF.CSS
{
    internal enum DisplayMode : byte
    {
        None,
        Inline,
        Block,
        ListItem,
        InlineBlock,
        InlineTable,
        Table,
        TableCaption,
        TableCell,
        TableColumn,
        TableColumnGroup,
        TableFooterGroup,
        TableHeaderGroup,
        TableRow,
        TableRowGroup,
        Flex,
        InlineFlex,
        Grid,
        InlineGrid,
        Contents,

        /// <summary>
        /// <c>flow-root</c> (css-display-3 §2.4): a block-level box that establishes an independent block formatting
        /// context. Laid out as a block everywhere (<see cref="PeachPDF.Html.Core.Dom.DerivedStyle.ActualDisplay"/>
        /// reads <c>block</c>); only the formatting-context predicates tell it apart.
        /// </summary>
        FlowRoot
    }
}