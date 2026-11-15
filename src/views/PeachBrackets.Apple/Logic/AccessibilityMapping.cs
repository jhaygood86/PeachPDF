using PeachBrackets.Accessibility;

namespace PeachBrackets.Apple.Logic;

/// <summary>What VoiceOver is told a part of the page is, as UIKit's traits.</summary>
[Flags]
internal enum AppleTraits
{
    None = 0,
    Button = 1,
    Link = 2,
    Header = 4,
    Image = 8,
    StaticText = 16,
    Adjustable = 32,
    Selected = 64,
    NotEnabled = 128,
    SearchField = 256,
}

/// <summary>What an assistive technology is told about a part of the page, as Apple words it. Apart from UIKit and AppKit so it can be tested without them.</summary>
internal static class AppleAccessibility
{
    /// <summary>The UIKit traits of a role, plus the ones its state adds.</summary>
    public static AppleTraits Traits(AccessibilityRole role, bool selected, bool disabled)
    {
        var traits = role switch
        {
            AccessibilityRole.Button or AccessibilityRole.CheckBox or AccessibilityRole.Radio or AccessibilityRole.Switch or AccessibilityRole.Tab
                or AccessibilityRole.MenuItem or AccessibilityRole.MenuItemCheckBox or AccessibilityRole.MenuItemRadio or AccessibilityRole.ComboBox => AppleTraits.Button,
            AccessibilityRole.Link => AppleTraits.Link,
            AccessibilityRole.Heading => AppleTraits.Header,
            AccessibilityRole.Image or AccessibilityRole.GraphicsDocument or AccessibilityRole.Math => AppleTraits.Image,
            AccessibilityRole.Slider or AccessibilityRole.SpinButton or AccessibilityRole.ScrollBar or AccessibilityRole.ProgressBar or AccessibilityRole.Meter => AppleTraits.Adjustable,
            AccessibilityRole.SearchBox => AppleTraits.SearchField,
            _ => AppleTraits.StaticText,
        };

        return traits | (selected ? AppleTraits.Selected : 0) | (disabled ? AppleTraits.NotEnabled : 0);
    }

    /// <summary>The AppKit role (an <c>NSAccessibilityRole</c> string) of a role.</summary>
    public static string MacRole(AccessibilityRole role) => role switch
    {
        AccessibilityRole.Button or AccessibilityRole.Tab or AccessibilityRole.MenuItem => "AXButton",
        AccessibilityRole.CheckBox or AccessibilityRole.Switch or AccessibilityRole.MenuItemCheckBox => "AXCheckBox",
        AccessibilityRole.Radio or AccessibilityRole.MenuItemRadio => "AXRadioButton",
        AccessibilityRole.Link => "AXLink",
        AccessibilityRole.Heading => "AXHeading",
        AccessibilityRole.Image or AccessibilityRole.GraphicsDocument or AccessibilityRole.Math => "AXImage",
        AccessibilityRole.TextBox or AccessibilityRole.SearchBox => "AXTextField",
        AccessibilityRole.ComboBox => "AXPopUpButton",
        AccessibilityRole.Slider => "AXSlider",
        AccessibilityRole.ProgressBar or AccessibilityRole.Meter => "AXLevelIndicator",
        AccessibilityRole.List or AccessibilityRole.ListBox or AccessibilityRole.Directory or AccessibilityRole.Feed => "AXList",
        AccessibilityRole.Table or AccessibilityRole.Grid or AccessibilityRole.TreeGrid => "AXTable",
        _ => "AXStaticText",
    };

    /// <summary>
    /// Whether a part of the page is something VoiceOver stops on: a control, a link, a heading or an image, or a piece of text with nothing
    /// inside it that is one. Containers (a section, a list, a table) are only grouping, so the reading order is the leaves.
    /// </summary>
    public static bool IsElement(AccessibilityRole role, bool hasChildren, bool hasText) =>
        hasText && (!hasChildren || role is AccessibilityRole.Button or AccessibilityRole.Link or AccessibilityRole.Heading or AccessibilityRole.CheckBox
            or AccessibilityRole.Radio or AccessibilityRole.Switch or AccessibilityRole.Tab or AccessibilityRole.MenuItem or AccessibilityRole.ComboBox
            or AccessibilityRole.TextBox or AccessibilityRole.SearchBox or AccessibilityRole.Slider);

    /// <summary>Whether activating (a double-tap, VO-Space) a role does something.</summary>
    public static bool IsActivatable(AccessibilityRole role) =>
        role is AccessibilityRole.Link or AccessibilityRole.Button or AccessibilityRole.CheckBox or AccessibilityRole.Radio or AccessibilityRole.Switch
            or AccessibilityRole.Tab or AccessibilityRole.MenuItem or AccessibilityRole.MenuItemCheckBox or AccessibilityRole.MenuItemRadio;

    /// <summary>What a link's target is: an address, or a place in the document (a fragment).</summary>
    public static (string? Uri, string? Destination) LinkTarget(string uri) => uri.StartsWith('#') ? (null, uri[1..]) : (uri, null);
}
