using PeachPDF.Html.Core.Utils;

namespace PeachPDF.Html.Core.Entities
{
    internal class CssDisplay
    {
        public static CssDisplay Block => new(CssConstants.Block);
        public static CssDisplay Inline => new(CssConstants.Inline);
        public static CssDisplay InlineTable = new(CssConstants.InlineTable);
        public static CssDisplay Flex = new(CssConstants.Flex);
        public static CssDisplay Grid = new(CssConstants.Grid);
        public static CssDisplay Table = new(CssConstants.Table);
        public static CssDisplay TableCell = new(CssConstants.TableCell);
        public static CssDisplay TableRow = new(CssConstants.TableRow);
        public static CssDisplay None = new(CssConstants.None);

        public CssDisplay(string value)
        {
            Value = value;

            var keywords = Value.Split(' ');

            var isOutsideSet = false;
            var isInsideSet = false;

            DisplayOutside = null;
            DisplayInside = null;
            IsInternal = false;

            foreach (var keyword in keywords)
            {
                switch (keyword)
                {
                    case CssConstants.Block:
                        isOutsideSet = true;
                        DisplayOutside = CssDisplayOutside.Block;
                        break;
                    case CssConstants.Inline:
                        isOutsideSet = true;
                        DisplayOutside = CssDisplayOutside.Inline;
                        break;
                    case CssConstants.Flow:
                        isInsideSet = true;
                        DisplayInside = CssDisplayInside.Flow;
                        break;
                    case CssConstants.FlowRoot:
                        isInsideSet = true;
                        DisplayInside = CssDisplayInside.FlowRoot;
                        break;
                    case CssConstants.Table:
                        isInsideSet = true;
                        DisplayInside = CssDisplayInside.Table;
                        break;
                    case CssConstants.Flex:
                        isInsideSet = true;
                        DisplayInside = CssDisplayInside.Flex;
                        break;
                    case CssConstants.Grid:
                        isInsideSet = true;
                        DisplayInside = CssDisplayInside.Grid;
                        break;
                    case CssConstants.Ruby:
                        isInsideSet = true;
                        DisplayInside = CssDisplayInside.Ruby;
                        break;
                    case CssConstants.ListItem:
                        IsListItem = true;
                        break;
                    case CssConstants.TableRowGroup:
                    case CssConstants.TableHeaderGroup:
                    case CssConstants.TableFooterGroup:
                    case CssConstants.TableRow:
                    case CssConstants.TableCell:
                    case CssConstants.TableColumnGroup:
                    case CssConstants.TableColumn:
                    case CssConstants.TableCaption:
                    case CssConstants.RubyBase:
                    case CssConstants.RubyBaseContainer:
                    case CssConstants.RubyText:
                    case CssConstants.RubyTextContainer:
                        IsInternal = true;
                        break;
                    case CssConstants.InlineBlock:
                        isOutsideSet = true;
                        isInsideSet = true;
                        DisplayOutside = CssDisplayOutside.Inline;
                        DisplayInside = CssDisplayInside.FlowRoot;
                        break;
                    case CssConstants.InlineTable:
                        isOutsideSet = true;
                        isInsideSet = true;
                        DisplayOutside = CssDisplayOutside.Inline;
                        DisplayInside = CssDisplayInside.Table;
                        break;
                    case CssConstants.InlineFlex:
                        isOutsideSet = true;
                        isInsideSet = true;
                        DisplayOutside = CssDisplayOutside.Inline;
                        DisplayInside = CssDisplayInside.Flex;
                        break;
                    case CssConstants.InlineGrid:
                        isOutsideSet = true;
                        isInsideSet = true;
                        DisplayOutside = CssDisplayOutside.Inline;
                        DisplayInside = CssDisplayInside.Grid;
                        break;
                    case CssConstants.None:
                        DisplayBox = CssDisplayBox.None;
                        break;
                    case CssConstants.Contents:
                        DisplayBox = CssDisplayBox.Contents;
                        break;
                }

                if (isOutsideSet && !isInsideSet)
                {
                    DisplayInside = CssDisplayInside.Flow;
                }

                if (isInsideSet && !isOutsideSet)
                {
                    DisplayOutside = CssDisplayOutside.Block;
                }
            }
        }

        public string Value { get; init; }

        public CssDisplayOutside? DisplayOutside { get; init; }

        public CssDisplayInside? DisplayInside { get; init; }

        public CssDisplayBox? DisplayBox { get; init; }

        public bool IsListItem { get; init; }

        public bool IsInternal { get; init; }

        public bool IsTable
        {
            get
            {
                if (DisplayInside == CssDisplayInside.Table)
                {
                    return true;
                }

                return IsInternal && Value.StartsWith("table-");
            }
        }


        public CssDisplay GetDisplay(bool isFloat)
        {
            if (!isFloat) return this;

            if (DisplayOutside != CssDisplayOutside.Inline) return Block;

            return DisplayInside switch
            {
                CssDisplayInside.Table => Table,
                CssDisplayInside.Flex => Flex,
                CssDisplayInside.Grid => Grid,
                _ => Block
            };
        }

        public override string ToString()
        {
            return Value;
        }

        public enum CssDisplayOutside
        {
            Block,
            Inline
        }

        public enum CssDisplayInside
        {
            ListItem,
            Flow,
            FlowRoot,
            Table,
            Flex,
            Grid,
            Ruby
        }

        public enum CssDisplayBox
        {
            Contents,
            None
        }
    }
}
