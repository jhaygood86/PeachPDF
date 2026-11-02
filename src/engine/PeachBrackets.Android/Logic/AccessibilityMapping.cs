using PeachBrackets.Accessibility;

namespace PeachBrackets.Android.Logic;

/// <summary>What an accessibility service is told about a part of the page, as Android words it. Apart from Android so it can be tested without it.</summary>
internal static class AccessibilityMapping
{
    /// <summary>
    /// The class name of the widget a role is like, which is how TalkBack decides what to say ("button", "check box", "edit box") and which actions
    /// it offers.
    /// </summary>
    public static string ClassName(AccessibilityRole role) => role switch
    {
        AccessibilityRole.Button => "android.widget.Button",
        AccessibilityRole.CheckBox or AccessibilityRole.MenuItemCheckBox => "android.widget.CheckBox",
        AccessibilityRole.Switch => "android.widget.Switch",
        AccessibilityRole.Radio or AccessibilityRole.MenuItemRadio => "android.widget.RadioButton",
        AccessibilityRole.TextBox or AccessibilityRole.SearchBox => "android.widget.EditText",
        AccessibilityRole.ComboBox => "android.widget.Spinner",
        AccessibilityRole.Slider or AccessibilityRole.ScrollBar => "android.widget.SeekBar",
        AccessibilityRole.ProgressBar or AccessibilityRole.Meter => "android.widget.ProgressBar",
        AccessibilityRole.Image or AccessibilityRole.GraphicsDocument => "android.widget.ImageView",
        AccessibilityRole.List or AccessibilityRole.ListBox or AccessibilityRole.Directory or AccessibilityRole.Feed => "android.widget.ListView",
        AccessibilityRole.Table or AccessibilityRole.Grid or AccessibilityRole.TreeGrid => "android.widget.GridView",
        AccessibilityRole.Heading or AccessibilityRole.Paragraph or AccessibilityRole.Text or AccessibilityRole.Link or AccessibilityRole.ListItem
            or AccessibilityRole.Option or AccessibilityRole.Cell or AccessibilityRole.GridCell or AccessibilityRole.ColumnHeader
            or AccessibilityRole.RowHeader or AccessibilityRole.Caption => "android.widget.TextView",
        _ => "android.view.View",
    };

    /// <summary>Whether a role is something that can be switched on and off, and so has a checked state to report.</summary>
    public static bool IsCheckable(AccessibilityRole role) =>
        role is AccessibilityRole.CheckBox or AccessibilityRole.Radio or AccessibilityRole.Switch or AccessibilityRole.MenuItemCheckBox or AccessibilityRole.MenuItemRadio;

    /// <summary>Whether a role is something a person can act on with a tap.</summary>
    public static bool IsClickable(AccessibilityRole role) =>
        role is AccessibilityRole.Link or AccessibilityRole.Button or AccessibilityRole.CheckBox or AccessibilityRole.Radio or AccessibilityRole.Switch
            or AccessibilityRole.MenuItem or AccessibilityRole.MenuItemCheckBox or AccessibilityRole.MenuItemRadio or AccessibilityRole.Tab;

    /// <summary>The value of <c>AccessibilityNodeInfo.getLiveRegion()</c> for a live setting: none, polite or assertive.</summary>
    public static int LiveRegion(LiveSetting live) => live switch
    {
        LiveSetting.Polite => 1,
        LiveSetting.Assertive => 2,
        _ => 0,
    };
}
