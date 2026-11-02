using Microsoft.UI.Xaml.Automation.Peers;
using PeachBrackets.Accessibility;
using PeachBrackets.WinUI;

namespace PeachBrackets.WinUI.Tests;

public class AutomationMappingTests
{
    [Theory]
    [InlineData(AccessibilityRole.Document, AutomationControlType.Document)]
    [InlineData(AccessibilityRole.Link, AutomationControlType.Hyperlink)]
    [InlineData(AccessibilityRole.ListItem, AutomationControlType.ListItem)]
    [InlineData(AccessibilityRole.Option, AutomationControlType.ListItem)]
    [InlineData(AccessibilityRole.List, AutomationControlType.List)]
    [InlineData(AccessibilityRole.ListBox, AutomationControlType.List)]
    [InlineData(AccessibilityRole.Button, AutomationControlType.Button)]
    [InlineData(AccessibilityRole.CheckBox, AutomationControlType.CheckBox)]
    [InlineData(AccessibilityRole.Switch, AutomationControlType.CheckBox)]
    [InlineData(AccessibilityRole.Radio, AutomationControlType.RadioButton)]
    [InlineData(AccessibilityRole.TextBox, AutomationControlType.Edit)]
    [InlineData(AccessibilityRole.SearchBox, AutomationControlType.Edit)]
    [InlineData(AccessibilityRole.ComboBox, AutomationControlType.ComboBox)]
    [InlineData(AccessibilityRole.Slider, AutomationControlType.Slider)]
    [InlineData(AccessibilityRole.SpinButton, AutomationControlType.Spinner)]
    [InlineData(AccessibilityRole.ProgressBar, AutomationControlType.ProgressBar)]
    [InlineData(AccessibilityRole.Meter, AutomationControlType.ProgressBar)]
    [InlineData(AccessibilityRole.Image, AutomationControlType.Image)]
    [InlineData(AccessibilityRole.Table, AutomationControlType.Table)]
    [InlineData(AccessibilityRole.Grid, AutomationControlType.DataGrid)]
    [InlineData(AccessibilityRole.Row, AutomationControlType.DataItem)]
    [InlineData(AccessibilityRole.ColumnHeader, AutomationControlType.HeaderItem)]
    [InlineData(AccessibilityRole.RowHeader, AutomationControlType.HeaderItem)]
    [InlineData(AccessibilityRole.Separator, AutomationControlType.Separator)]
    [InlineData(AccessibilityRole.Tab, AutomationControlType.TabItem)]
    [InlineData(AccessibilityRole.TabList, AutomationControlType.Tab)]
    [InlineData(AccessibilityRole.Menu, AutomationControlType.Menu)]
    [InlineData(AccessibilityRole.MenuBar, AutomationControlType.MenuBar)]
    [InlineData(AccessibilityRole.MenuItem, AutomationControlType.MenuItem)]
    [InlineData(AccessibilityRole.ToolBar, AutomationControlType.ToolBar)]
    [InlineData(AccessibilityRole.ToolTip, AutomationControlType.ToolTip)]
    [InlineData(AccessibilityRole.Tree, AutomationControlType.Tree)]
    [InlineData(AccessibilityRole.TreeItem, AutomationControlType.TreeItem)]
    [InlineData(AccessibilityRole.Heading, AutomationControlType.Text)]
    [InlineData(AccessibilityRole.Paragraph, AutomationControlType.Text)]
    [InlineData(AccessibilityRole.Text, AutomationControlType.Text)]
    [InlineData(AccessibilityRole.Cell, AutomationControlType.Text)]
    [InlineData(AccessibilityRole.Navigation, AutomationControlType.Group)]
    [InlineData(AccessibilityRole.Group, AutomationControlType.Group)]
    [InlineData(AccessibilityRole.Figure, AutomationControlType.Group)]
    public void ARoleIsToldAsTheControlTypeItIsLike(AccessibilityRole role, AutomationControlType expected) =>
        Assert.Equal(expected, HtmlViewLogic.ToControlType(role));

    [Theory]
    [InlineData(AccessibilityRole.Main, AutomationLandmarkType.Main)]
    [InlineData(AccessibilityRole.Navigation, AutomationLandmarkType.Navigation)]
    [InlineData(AccessibilityRole.Search, AutomationLandmarkType.Search)]
    [InlineData(AccessibilityRole.Form, AutomationLandmarkType.Form)]
    [InlineData(AccessibilityRole.Banner, AutomationLandmarkType.Custom)]
    [InlineData(AccessibilityRole.ContentInfo, AutomationLandmarkType.Custom)]
    [InlineData(AccessibilityRole.Complementary, AutomationLandmarkType.Custom)]
    [InlineData(AccessibilityRole.Region, AutomationLandmarkType.Custom)]
    [InlineData(AccessibilityRole.Paragraph, AutomationLandmarkType.None)]
    [InlineData(AccessibilityRole.Group, AutomationLandmarkType.None)]
    public void OnlyLandmarksHaveALandmarkType(AccessibilityRole role, AutomationLandmarkType expected) =>
        Assert.Equal(expected, HtmlViewLogic.ToLandmarkType(role));

    [Theory]
    [InlineData(AccessibilityRole.Banner, "banner")]
    [InlineData(AccessibilityRole.ContentInfo, "content information")]
    [InlineData(AccessibilityRole.Complementary, "complementary")]
    [InlineData(AccessibilityRole.Region, "region")]
    [InlineData(AccessibilityRole.Main, null)]
    [InlineData(AccessibilityRole.Paragraph, null)]
    public void ALandmarkWithoutATypeOfItsOwnIsNamedByWhatItIs(AccessibilityRole role, string? expected) =>
        Assert.Equal(expected, HtmlViewLogic.LandmarkName(role));

    [Theory]
    [InlineData(LiveSetting.Off, AutomationLiveSetting.Off)]
    [InlineData(LiveSetting.Polite, AutomationLiveSetting.Polite)]
    [InlineData(LiveSetting.Assertive, AutomationLiveSetting.Assertive)]
    public void ALiveRegionIsToldWithTheSameUrgency(LiveSetting live, AutomationLiveSetting expected) =>
        Assert.Equal(expected, HtmlViewLogic.ToLiveSetting(live));

    [Theory]
    [InlineData(AccessibilityRole.CheckBox, true)]
    [InlineData(AccessibilityRole.Radio, true)]
    [InlineData(AccessibilityRole.Switch, true)]
    [InlineData(AccessibilityRole.MenuItemCheckBox, true)]
    [InlineData(AccessibilityRole.Button, false)]
    [InlineData(AccessibilityRole.Link, false)]
    public void ARoleIsToggledWhenItIsAChoiceThatIsOnOrOff(AccessibilityRole role, bool expected) =>
        Assert.Equal(expected, HtmlViewLogic.IsToggleControl(role));

    [Theory]
    [InlineData(PeachBrackets.Accessibility.ToggleState.True, Microsoft.UI.Xaml.Automation.ToggleState.On)]
    [InlineData(PeachBrackets.Accessibility.ToggleState.Mixed, Microsoft.UI.Xaml.Automation.ToggleState.Indeterminate)]
    [InlineData(PeachBrackets.Accessibility.ToggleState.False, Microsoft.UI.Xaml.Automation.ToggleState.Off)]
    [InlineData(PeachBrackets.Accessibility.ToggleState.None, Microsoft.UI.Xaml.Automation.ToggleState.Off)]
    public void ACheckedStateIsAToggleState(PeachBrackets.Accessibility.ToggleState state, Microsoft.UI.Xaml.Automation.ToggleState expected) =>
        Assert.Equal(expected, HtmlViewLogic.ToToggleState(state));
}
