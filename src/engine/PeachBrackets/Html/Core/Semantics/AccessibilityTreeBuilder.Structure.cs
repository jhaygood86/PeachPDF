using PeachBrackets.Accessibility;
using PeachBrackets.Html.Dom;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PeachBrackets.Html.Semantics
{
    internal sealed partial class AccessibilityTreeBuilder
    {
        private static readonly string[] ReferenceAttributes =
        [
            "aria-controls", "aria-owns", "aria-flowto", "aria-details", "aria-errormessage", "aria-labelledby", "aria-describedby",
            "aria-activedescendant", "headers",
        ];

        // What an element says about where it is in a table or a set, and which other elements it points at, read as the node is made.
        private static void ReadStructureAttributes(AccessibilityNode node, HtmlTag tag)
        {
            node.ColumnSpan = Span(tag, "colspan", "aria-colspan");
            node.RowSpan = Span(tag, "rowspan", "aria-rowspan");
            node.ExplicitRowIndex = Number(tag, "aria-rowindex") is { } rowIndex and >= 1 ? rowIndex - 1 : null;
            node.ExplicitColumnIndex = Number(tag, "aria-colindex") is { } colIndex and >= 1 ? colIndex - 1 : null;
            node.ExplicitRowCount = Number(tag, "aria-rowcount");
            node.ExplicitColumnCount = Number(tag, "aria-colcount");
            node.ExplicitPosition = Number(tag, "aria-posinset") is { } position and >= 1 ? position : null;
            node.ExplicitSetSize = Number(tag, "aria-setsize");

            foreach (var attribute in ReferenceAttributes)
            {
                if (NonEmpty(tag.TryGetAttribute(attribute)) is { } ids)
                    (node.RawReferences ??= [])[attribute] = ids;
            }
        }

        // The number a progress bar, meter, slider, scroll bar or spin button stands at and the range it can be in.
        private static void ReadRange(AccessibilityNode node, HtmlTag tag, AccessibilityRole role)
        {
            if (role is not (AccessibilityRole.ProgressBar or AccessibilityRole.Meter or AccessibilityRole.Slider or AccessibilityRole.ScrollBar or AccessibilityRole.SpinButton))
                return;

            var element = tag.Name.ToLowerInvariant();
            var native = element is "progress" or "meter" || (element == "input" && tag.TryGetAttribute("type")?.Trim().ToLowerInvariant() is "range" or "number");
            var fraction = element is "progress" or "meter";

            node.ValueMin = Decimal(tag, native ? "min" : null) ?? Decimal(tag, "aria-valuemin") ?? 0;
            node.ValueMax = Decimal(tag, native ? "max" : null) ?? Decimal(tag, "aria-valuemax") ?? (fraction ? 1 : 100);
            if (node.ValueMax < node.ValueMin)
                node.ValueMax = node.ValueMin;

            var value = Decimal(tag, native ? "value" : null) ?? Decimal(tag, "aria-valuenow");
            if (value is null && element == "input" && tag.TryGetAttribute("type")?.Trim().Equals("range", StringComparison.OrdinalIgnoreCase) == true)
                value = node.ValueMin + ((node.ValueMax - node.ValueMin) / 2);

            node.Value = value is { } v ? Math.Clamp(v, node.ValueMin, node.ValueMax) : null;
            node.ValueText = NonEmpty(tag.TryGetAttribute("aria-valuetext")) ?? string.Empty;
        }

        private static double? Decimal(HtmlTag tag, string? attribute) =>
            attribute is not null && double.TryParse(tag.TryGetAttribute(attribute)?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value)
                ? value
                : null;

        private static int? Number(HtmlTag tag, string attribute) =>
            int.TryParse(tag.TryGetAttribute(attribute)?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;

        // A cell covers one row or column unless it says more; zero (to the end of the group) is taken as one, since a reader needs a size.
        private static int Span(HtmlTag tag, string native, string aria) =>
            (Number(tag, native) ?? Number(tag, aria)) is { } value and > 1 ? Math.Min(value, 1000) : 1;

        // Once the whole tree is built: the places in tables and sets, and the nodes that others refer to.
        private static void ResolveStructure(AccessibilityNode root)
        {
            var all = root.DescendantsAndSelf().ToArray();
            var byElementId = new Dictionary<string, AccessibilityNode>(StringComparer.Ordinal);
            foreach (var node in all)
            {
                if (node.ElementId is { } id)
                    byElementId.TryAdd(id, node);
            }

            foreach (var node in all)
            {
                if (node.Role is AccessibilityRole.Table or AccessibilityRole.Grid or AccessibilityRole.TreeGrid)
                    NumberTable(node, byElementId);

                NumberSets(node);
                ResolveReferences(node, byElementId);
            }
        }

        // ---- tables ------------------------------------------------------------------------------------------------------------------

        private static bool IsCell(AccessibilityRole role) =>
            role is AccessibilityRole.Cell or AccessibilityRole.GridCell or AccessibilityRole.ColumnHeader or AccessibilityRole.RowHeader;

        // The rows of a table in order, through its row groups but not into a table inside it.
        private static void CollectRows(AccessibilityNode node, List<AccessibilityNode> rows)
        {
            foreach (var child in node.Children)
            {
                if (child.Role == AccessibilityRole.Row)
                    rows.Add(child);
                else if (child.Role is not (AccessibilityRole.Table or AccessibilityRole.Grid or AccessibilityRole.TreeGrid))
                    CollectRows(child, rows);
            }
        }

        private static void CollectCells(AccessibilityNode row, List<AccessibilityNode> cells)
        {
            foreach (var child in row.Children)
            {
                if (IsCell(child.Role))
                    cells.Add(child);
                else if (child.Role != AccessibilityRole.Row)
                    CollectCells(child, cells);
            }
        }

        // Puts each cell at its row and column the way HTML lays a table out: cells go left to right in their row, skipping places a cell from
        // a row above reaches down into, and a cell with a span takes that many places.
        private static void NumberTable(AccessibilityNode table, Dictionary<string, AccessibilityNode> byElementId)
        {
            var rows = new List<AccessibilityNode>();
            CollectRows(table, rows);

            var occupied = new HashSet<(int Row, int Column)>();
            var cells = new List<AccessibilityNode>();
            var columns = 0;
            for (var r = 0; r < rows.Count; r++)
            {
                var row = rows[r];
                row.RowIndex = row.ExplicitRowIndex ?? r;
                var rowCells = new List<AccessibilityNode>();
                CollectCells(row, rowCells);

                var column = 0;
                foreach (var cell in rowCells)
                {
                    while (occupied.Contains((r, column)))
                        column++;

                    cell.RowIndex = cell.ExplicitRowIndex ?? row.RowIndex;
                    cell.ColumnIndex = cell.ExplicitColumnIndex ?? column;
                    for (var dr = 0; dr < cell.RowSpan; dr++)
                    {
                        for (var dc = 0; dc < cell.ColumnSpan; dc++)
                            occupied.Add((r + dr, column + dc));
                    }

                    column += cell.ColumnSpan;
                    columns = Math.Max(columns, column);
                    cells.Add(cell);
                }
            }

            table.RowCount = table.ExplicitRowCount is { } rowCount and > 0 ? rowCount : rows.Count;
            table.ColumnCount = table.ExplicitColumnCount is { } columnCount and > 0 ? columnCount : columns;

            AssociateHeaders(cells, byElementId);
        }

        // Which cells head a data cell: those the cell's headers attribute names, else the column headers above it and the row headers before it.
        private static void AssociateHeaders(List<AccessibilityNode> cells, Dictionary<string, AccessibilityNode> byElementId)
        {
            var columnHeaders = cells.Where(c => c.Role == AccessibilityRole.ColumnHeader).ToList();
            var rowHeaders = cells.Where(c => c.Role == AccessibilityRole.RowHeader).ToList();
            if (columnHeaders.Count == 0 && rowHeaders.Count == 0)
                return;

            foreach (var cell in cells)
            {
                if (cell.Role is not (AccessibilityRole.Cell or AccessibilityRole.GridCell))
                    continue;

                if (cell.RawReferences is { } references && references.TryGetValue("headers", out var ids))
                {
                    var named = ids.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                        .Select(id => byElementId.GetValueOrDefault(id)).OfType<AccessibilityNode>().Where(h => cells.Contains(h)).ToList();
                    if (named.Count > 0)
                    {
                        cell.ColumnHeaders = named.Where(h => h.Role == AccessibilityRole.ColumnHeader).ToArray();
                        cell.RowHeaders = named.Where(h => h.Role == AccessibilityRole.RowHeader).ToArray();
                        continue;
                    }
                }

                cell.ColumnHeaders = columnHeaders
                    .Where(h => h.RowIndex < cell.RowIndex && h.ColumnIndex < cell.ColumnIndex + cell.ColumnSpan && h.ColumnIndex + h.ColumnSpan > cell.ColumnIndex)
                    .ToArray();
                cell.RowHeaders = rowHeaders
                    .Where(h => h.ColumnIndex < cell.ColumnIndex && h.RowIndex < cell.RowIndex + cell.RowSpan && h.RowIndex + h.RowSpan > cell.RowIndex)
                    .ToArray();
            }
        }

        // ---- sets --------------------------------------------------------------------------------------------------------------------

        private static bool IsSetItem(AccessibilityRole role) =>
            role is AccessibilityRole.ListItem or AccessibilityRole.Option or AccessibilityRole.Tab or AccessibilityRole.TreeItem
                or AccessibilityRole.MenuItem or AccessibilityRole.MenuItemCheckBox or AccessibilityRole.MenuItemRadio;

        // The items of a set are the nodes of the same role below the same parent; the author can say otherwise with aria-posinset and aria-setsize.
        private static void NumberSets(AccessibilityNode parent)
        {
            foreach (var group in parent.Children.Where(c => IsSetItem(c.Role)).GroupBy(c => c.Role))
            {
                var items = group.ToArray();
                var size = items.Select(i => i.ExplicitSetSize).FirstOrDefault(n => n is > 0) ?? items.Length;
                var position = 0;
                foreach (var item in items)
                {
                    position = item.ExplicitPosition ?? position + 1;
                    item.PositionInSet = position;
                    item.SetSize = size;
                }
            }
        }

        // ---- relations ---------------------------------------------------------------------------------------------------------------

        private static void ResolveReferences(AccessibilityNode node, Dictionary<string, AccessibilityNode> byElementId)
        {
            if (node.RawReferences is not { } references)
                return;

            IReadOnlyList<AccessibilityNode> Resolve(string attribute)
            {
                if (!references.TryGetValue(attribute, out var ids))
                    return [];

                var found = new List<AccessibilityNode>();
                foreach (var id in ids.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
                {
                    if (byElementId.TryGetValue(id, out var target) && !ReferenceEquals(target, node) && !found.Contains(target))
                        found.Add(target);
                }

                return found;
            }

            node.Controls = Resolve("aria-controls");
            node.Owns = Resolve("aria-owns");
            node.FlowTo = Resolve("aria-flowto");
            node.Details = Resolve("aria-details");
            node.ErrorMessages = Resolve("aria-errormessage");
            node.LabelledBy = Resolve("aria-labelledby");
            node.DescribedBy = Resolve("aria-describedby");
            node.ActiveDescendant = Resolve("aria-activedescendant").FirstOrDefault();
        }
    }
}
