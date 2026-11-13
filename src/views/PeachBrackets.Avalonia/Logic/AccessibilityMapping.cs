using Avalonia.Automation;
using Avalonia.Automation.Peers;
using PeachBrackets.Accessibility;

namespace PeachBrackets.Avalonia.Logic;

/// <summary>What the document's accessibility roles and states are told to Avalonia's automation as; the parts of the mapping that need no control.</summary>
internal static class AccessibilityMapping
{
    /// <summary>The automation control type a role is told to assistive technology as.</summary>
    /// <param name="role">the role of a node of the accessibility tree</param>
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
        AccessibilityRole.Slider => AutomationControlType.Slider,
        AccessibilityRole.ScrollBar => AutomationControlType.ScrollBar,
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

    /// <summary>The landmark a role is, or null for a role that is not one.</summary>
    /// <param name="role">the role of a node</param>
    public static AutomationLandmarkType? ToLandmarkType(AccessibilityRole role) => role switch
    {
        AccessibilityRole.Main => AutomationLandmarkType.Main,
        AccessibilityRole.Navigation => AutomationLandmarkType.Navigation,
        AccessibilityRole.Search => AutomationLandmarkType.Search,
        AccessibilityRole.Form => AutomationLandmarkType.Form,
        AccessibilityRole.Banner => AutomationLandmarkType.Banner,
        AccessibilityRole.ContentInfo => AutomationLandmarkType.ContentInfo,
        AccessibilityRole.Complementary => AutomationLandmarkType.Complementary,
        AccessibilityRole.Region => AutomationLandmarkType.Region,
        _ => null,
    };

    /// <summary>How a live region is told to assistive technology.</summary>
    /// <param name="live">the region's <c>aria-live</c> setting</param>
    public static AutomationLiveSetting ToLiveSetting(LiveSetting live) => live switch
    {
        LiveSetting.Polite => AutomationLiveSetting.Polite,
        LiveSetting.Assertive => AutomationLiveSetting.Assertive,
        _ => AutomationLiveSetting.Off,
    };

    /// <summary>The automation toggle state of a node's checked state.</summary>
    /// <param name="state">the state the document reports</param>
    public static global::Avalonia.Automation.Provider.ToggleState ToToggleState(PeachBrackets.Accessibility.ToggleState state) => state switch
    {
        PeachBrackets.Accessibility.ToggleState.True => global::Avalonia.Automation.Provider.ToggleState.On,
        PeachBrackets.Accessibility.ToggleState.Mixed => global::Avalonia.Automation.Provider.ToggleState.Indeterminate,
        _ => global::Avalonia.Automation.Provider.ToggleState.Off,
    };

    /// <summary>Whether a role is one automation toggles: a check box, a radio button, a switch.</summary>
    /// <param name="role">the role of a node</param>
    public static bool IsToggleControl(AccessibilityRole role) =>
        role is AccessibilityRole.CheckBox or AccessibilityRole.Radio or AccessibilityRole.Switch or AccessibilityRole.MenuItemCheckBox or AccessibilityRole.MenuItemRadio;

    /// <summary>Whether a role is read through the range-value pattern: a progress bar, meter, slider, scroll bar or spin button.</summary>
    /// <param name="role">the role of a node</param>
    public static bool IsRangeRole(AccessibilityRole role) =>
        role is AccessibilityRole.ProgressBar or AccessibilityRole.Meter or AccessibilityRole.Slider or AccessibilityRole.ScrollBar or AccessibilityRole.SpinButton;

    /// <summary>The size of a small step of a range (an arrow key) and of a large one (Page Up and Down): a hundredth and a tenth of its extent.</summary>
    /// <param name="minimum">the smallest value</param>
    /// <param name="maximum">the largest value</param>
    public static (double Small, double Large) RangeSteps(double minimum, double maximum)
    {
        var extent = Math.Max(maximum - minimum, 0);
        return (extent / 100, extent / 10);
    }

    /// <summary>Whether a role is a text field, read and set through the value pattern.</summary>
    /// <param name="role">the role of a node</param>
    public static bool IsTextField(AccessibilityRole role) => role is AccessibilityRole.TextBox or AccessibilityRole.SearchBox;
}
