namespace PeachBrackets.Css
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

        /// <summary><c>grid-lanes</c> (css-grid-3 §4): a grid whose items pack into the shortest lane. Reads <c>grid</c> through <c>ActualDisplay</c>; <c>CssLayoutEngineGridLanes</c> tells it apart.</summary>
        GridLanes,

        /// <summary><c>inline-grid-lanes</c>: the inline-level <see cref="GridLanes"/>.</summary>
        InlineGridLanes,
        Contents,

        /// <summary>
        /// <c>flow-root</c> (css-display-3 §2.4): a block-level box that establishes an independent block formatting
        /// context. Laid out as a block everywhere (<see cref="PeachBrackets.Html.Dom.DerivedStyle.ActualDisplay"/>
        /// reads <c>block</c>); only the formatting-context predicates tell it apart.
        /// </summary>
        FlowRoot
    }
}