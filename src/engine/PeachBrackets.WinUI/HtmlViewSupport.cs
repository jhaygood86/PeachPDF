using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Automation.Peers;
using PeachBrackets.Accessibility;
using PeachDrawing.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PeachBrackets.WinUI;

/// <summary>Raised when a link in an <see cref="HtmlView"/> is clicked or tapped.</summary>
public sealed class HtmlLinkClickedEventArgs : EventArgs
{
    internal HtmlLinkClickedEventArgs(string? uri, string? destination)
    {
        Uri = uri;
        Destination = destination;
    }

    /// <summary>The address the link leads to, or <see langword="null"/> for a link to a place in the document.</summary>
    public string? Uri { get; }

    /// <summary>The name of the place in the document the link leads to, or <see langword="null"/> for a link to an address.</summary>
    public string? Destination { get; }

    /// <summary>Set to <see langword="true"/> to stop the view following the link, for example because the application handles it.</summary>
    public bool Cancel { get; set; }
}

/// <summary>Raised when an <see cref="HtmlView"/> could not load or lay out its document.</summary>
public sealed class HtmlLoadFailedEventArgs : EventArgs
{
    internal HtmlLoadFailedEventArgs(Exception exception) => Exception = exception;

    /// <summary>What went wrong.</summary>
    public Exception Exception { get; }
}

/// <summary>The pieces of <see cref="HtmlView"/> that do not need a window, kept apart so they can be tested without one.</summary>
internal static class HtmlViewLogic
{
    /// <summary>The UI Automation control type a role is told to a screen reader as.</summary>
    public static AutomationControlType ToControlType(AccessibilityRole role) => role switch
    {
        AccessibilityRole.Document => AutomationControlType.Document,
        AccessibilityRole.Link => AutomationControlType.Hyperlink,
        AccessibilityRole.ListItem or AccessibilityRole.Option => AutomationControlType.ListItem,
        AccessibilityRole.List or AccessibilityRole.ListBox or AccessibilityRole.Directory or AccessibilityRole.Feed => AutomationControlType.List,
        AccessibilityRole.Button => AutomationControlType.Button,
        AccessibilityRole.CheckBox or AccessibilityRole.Switch or AccessibilityRole.MenuItemCheckBox => AutomationControlType.CheckBox,
        AccessibilityRole.Radio or AccessibilityRole.MenuItemRadio => AutomationControlType.RadioButton,
        AccessibilityRole.TextBox or AccessibilityRole.SearchBox => AutomationControlType.Edit,
        AccessibilityRole.ComboBox => AutomationControlType.ComboBox,
        AccessibilityRole.Slider or AccessibilityRole.ScrollBar => AutomationControlType.Slider,
        AccessibilityRole.SpinButton => AutomationControlType.Spinner,
        AccessibilityRole.ProgressBar or AccessibilityRole.Meter => AutomationControlType.ProgressBar,
        AccessibilityRole.Image or AccessibilityRole.GraphicsDocument => AutomationControlType.Image,
        AccessibilityRole.Table => AutomationControlType.Table,
        AccessibilityRole.Grid or AccessibilityRole.TreeGrid => AutomationControlType.DataGrid,
        AccessibilityRole.Row => AutomationControlType.DataItem,
        AccessibilityRole.ColumnHeader or AccessibilityRole.RowHeader => AutomationControlType.HeaderItem,
        AccessibilityRole.Separator => AutomationControlType.Separator,
        AccessibilityRole.Tab => AutomationControlType.TabItem,
        AccessibilityRole.TabList => AutomationControlType.Tab,
        AccessibilityRole.Menu => AutomationControlType.Menu,
        AccessibilityRole.MenuBar => AutomationControlType.MenuBar,
        AccessibilityRole.MenuItem => AutomationControlType.MenuItem,
        AccessibilityRole.ToolBar => AutomationControlType.ToolBar,
        AccessibilityRole.ToolTip => AutomationControlType.ToolTip,
        AccessibilityRole.Tree => AutomationControlType.Tree,
        AccessibilityRole.TreeItem => AutomationControlType.TreeItem,
        AccessibilityRole.Heading or AccessibilityRole.Paragraph or AccessibilityRole.Text or AccessibilityRole.Cell or AccessibilityRole.GridCell
            or AccessibilityRole.Caption or AccessibilityRole.Code or AccessibilityRole.Emphasis or AccessibilityRole.Strong or AccessibilityRole.Subscript
            or AccessibilityRole.Superscript or AccessibilityRole.Deletion or AccessibilityRole.Insertion or AccessibilityRole.Term or AccessibilityRole.Definition
            or AccessibilityRole.Time or AccessibilityRole.Math or AccessibilityRole.BlockQuote or AccessibilityRole.Note => AutomationControlType.Text,
        _ => AutomationControlType.Group,
    };

    /// <summary>The kind of landmark a role is, or <see cref="AutomationLandmarkType.None"/> for a role that is not one.</summary>
    public static AutomationLandmarkType ToLandmarkType(AccessibilityRole role) => role switch
    {
        AccessibilityRole.Main => AutomationLandmarkType.Main,
        AccessibilityRole.Navigation => AutomationLandmarkType.Navigation,
        AccessibilityRole.Search => AutomationLandmarkType.Search,
        AccessibilityRole.Form => AutomationLandmarkType.Form,
        AccessibilityRole.Banner or AccessibilityRole.ContentInfo or AccessibilityRole.Complementary or AccessibilityRole.Region => AutomationLandmarkType.Custom,
        _ => AutomationLandmarkType.None,
    };

    /// <summary>The name for a landmark that says what kind it is, for a role UI Automation has no landmark type for.</summary>
    public static string? LandmarkName(AccessibilityRole role) => role switch
    {
        AccessibilityRole.Banner => "banner",
        AccessibilityRole.ContentInfo => "content information",
        AccessibilityRole.Complementary => "complementary",
        AccessibilityRole.Region => "region",
        _ => null,
    };

    /// <summary>How a live region is told to a screen reader.</summary>
    public static AutomationLiveSetting ToLiveSetting(LiveSetting live) => live switch
    {
        LiveSetting.Polite => AutomationLiveSetting.Polite,
        LiveSetting.Assertive => AutomationLiveSetting.Assertive,
        _ => AutomationLiveSetting.Off,
    };

    /// <summary>The system pointer a <see cref="CursorShape"/> is shown as; the shapes WinUI has no pointer for fall back to the nearest one.</summary>
    public static InputSystemCursorShape ToSystemCursor(CursorShape shape) => shape switch
    {
        CursorShape.Pointer => InputSystemCursorShape.Hand,
        CursorShape.Text or CursorShape.VerticalText => InputSystemCursorShape.IBeam,
        CursorShape.Wait => InputSystemCursorShape.Wait,
        CursorShape.Help => InputSystemCursorShape.Help,
        CursorShape.Crosshair or CursorShape.Cell => InputSystemCursorShape.Cross,
        CursorShape.NotAllowed => InputSystemCursorShape.UniversalNo,
        CursorShape.Move or CursorShape.ScrollAll => InputSystemCursorShape.SizeAll,
        CursorShape.ResizeHorizontal or CursorShape.ResizeEast or CursorShape.ResizeWest or CursorShape.ResizeColumn => InputSystemCursorShape.SizeWestEast,
        CursorShape.ResizeVertical or CursorShape.ResizeNorth or CursorShape.ResizeSouth or CursorShape.ResizeRow => InputSystemCursorShape.SizeNorthSouth,
        CursorShape.ResizeDiagonalUp or CursorShape.ResizeNorthEast or CursorShape.ResizeSouthWest => InputSystemCursorShape.SizeNortheastSouthwest,
        CursorShape.ResizeDiagonalDown or CursorShape.ResizeNorthWest or CursorShape.ResizeSouthEast => InputSystemCursorShape.SizeNorthwestSoutheast,
        CursorShape.Progress => InputSystemCursorShape.AppStarting,
        _ => InputSystemCursorShape.Arrow,
    };

    /// <summary>The pointing devices a set of device kinds amounts to, as the <c>hover</c> and <c>pointer</c> media features report them.</summary>
    public static InputCapabilities Capabilities(bool mouse, bool pen, bool touch)
    {
        var any = (mouse || pen ? PointerTypes.Fine : PointerTypes.None) | (touch ? PointerTypes.Coarse : PointerTypes.None);
        var anyHover = mouse || pen;
        if (mouse || pen)
            return new InputCapabilities(PointerTypes.Fine, true, any, anyHover);

        return touch ? new InputCapabilities(PointerTypes.Coarse, false, any, anyHover) : InputCapabilities.None;
    }

    /// <summary>The page a vertical position falls on, with the position inside it.</summary>
    /// <param name="tops">the top of each page, in the view's units</param>
    /// <param name="heights">the height of each page, in the same units</param>
    /// <param name="y">the vertical position</param>
    /// <param name="localY">where the position is from the top of that page</param>
    /// <returns>the page index, or -1 when the position is between pages or outside them</returns>
    public static int PageAt(double[] tops, double[] heights, double y, out double localY)
    {
        for (var i = 0; i < tops.Length; i++)
        {
            if (y >= tops[i] && y < tops[i] + heights[i])
            {
                localY = y - tops[i];
                return i;
            }
        }

        localY = 0;
        return -1;
    }

    /// <summary>Whether a link may be opened by the system: only the schemes a document cannot abuse.</summary>
    /// <summary>Whether a role is one UI Automation reads through the RangeValue pattern: a progress bar, meter, slider, scroll bar or spin button.</summary>
    public static bool IsRangeRole(AccessibilityRole role) =>
        role is AccessibilityRole.ProgressBar or AccessibilityRole.Meter or AccessibilityRole.Slider or AccessibilityRole.ScrollBar or AccessibilityRole.SpinButton;

    /// <summary>The size of a small step of a range (an arrow key) and of a large one (Page Up and Down): a hundredth and a tenth of its extent.</summary>
    public static (double Small, double Large) RangeSteps(double minimum, double maximum)
    {
        var extent = Math.Max(maximum - minimum, 0);
        return (extent / 100, extent / 10);
    }
    /// <summary>How UI Automation processes a notification for a live region: an assertive one interrupts, a polite one waits for the reader to be idle.</summary>
    public static Microsoft.UI.Xaml.Automation.Peers.AutomationNotificationProcessing ToNotificationProcessing(LiveSetting setting) =>
        setting == LiveSetting.Assertive
            ? Microsoft.UI.Xaml.Automation.Peers.AutomationNotificationProcessing.ImportantMostRecent
            : Microsoft.UI.Xaml.Automation.Peers.AutomationNotificationProcessing.MostRecent;

    /// <summary>Whether a role is a table, grid or tree grid: the roles UI Automation reads through the Grid and Table patterns.</summary>
    public static bool IsGridRole(AccessibilityRole role) => role is AccessibilityRole.Table or AccessibilityRole.Grid or AccessibilityRole.TreeGrid;

    /// <summary>Whether a role is a cell of a table or grid, a header included: the roles UI Automation reads through the GridItem and TableItem patterns.</summary>
    public static bool IsGridCellRole(AccessibilityRole role) =>
        role is AccessibilityRole.Cell or AccessibilityRole.GridCell or AccessibilityRole.ColumnHeader or AccessibilityRole.RowHeader;

    /// <summary>The cells of a table in order, through its rows and row groups but not into a table inside it.</summary>
    public static IReadOnlyList<AccessibilityNode> CellsOf(AccessibilityNode table)
    {
        var cells = new List<AccessibilityNode>();
        void Visit(AccessibilityNode node)
        {
            foreach (var child in node.ReadingChildren)
            {
                if (IsGridCellRole(child.Role))
                    cells.Add(child);
                else if (!IsGridRole(child.Role))
                    Visit(child);
            }
        }

        Visit(table);
        return cells;
    }

    /// <summary>The cell that covers a place of a table - the cell itself, or the one whose span reaches it - or null when the place is empty.</summary>
    public static AccessibilityNode? CellAt(AccessibilityNode table, int row, int column)
    {
        foreach (var cell in CellsOf(table))
        {
            if (cell.RowIndex >= 0 && cell.ColumnIndex >= 0
                && row >= cell.RowIndex && row < cell.RowIndex + Math.Max(cell.RowSpan, 1)
                && column >= cell.ColumnIndex && column < cell.ColumnIndex + Math.Max(cell.ColumnSpan, 1))
            {
                return cell;
            }
        }

        return null;
    }

    /// <summary>The header cells of a table: those across its top (<paramref name="columns"/>) or down its side.</summary>
    public static IReadOnlyList<AccessibilityNode> HeadersOf(AccessibilityNode table, bool columns)
    {
        var role = columns ? AccessibilityRole.ColumnHeader : AccessibilityRole.RowHeader;
        return CellsOf(table).Where(c => c.Role == role).ToList();
    }

    /// <summary>The table or grid a cell belongs to, or null for a cell outside one.</summary>
    public static AccessibilityNode? TableOf(AccessibilityNode cell)
    {
        for (var parent = cell.Parent; parent is not null; parent = parent.Parent)
        {
            if (IsGridRole(parent.Role))
                return parent;
        }

        return null;
    }

    /// <summary>Whether a role is one UI Automation toggles: a check box, a radio button, a switch.</summary>
    public static bool IsToggleControl(AccessibilityRole role) =>
        role is AccessibilityRole.CheckBox or AccessibilityRole.Radio or AccessibilityRole.Switch or AccessibilityRole.MenuItemCheckBox or AccessibilityRole.MenuItemRadio;

    /// <summary>The UI Automation toggle state of a node's checked state.</summary>
    public static Microsoft.UI.Xaml.Automation.ToggleState ToToggleState(PeachBrackets.Accessibility.ToggleState state) => state switch
    {
        PeachBrackets.Accessibility.ToggleState.True => Microsoft.UI.Xaml.Automation.ToggleState.On,
        PeachBrackets.Accessibility.ToggleState.Mixed => Microsoft.UI.Xaml.Automation.ToggleState.Indeterminate,
        _ => Microsoft.UI.Xaml.Automation.ToggleState.Off,
    };

    public static bool IsLaunchable(string uri) =>
        Uri.TryCreate(uri, UriKind.Absolute, out var parsed)
        && parsed.Scheme is "http" or "https" or "mailto" or "tel";
}
