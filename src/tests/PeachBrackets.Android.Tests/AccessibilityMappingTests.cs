using PeachBrackets.Accessibility;
using PeachBrackets.Android.Logic;

namespace PeachBrackets.Android.Tests;

public class AccessibilityMappingTests
{
    [Theory]
    [InlineData(AccessibilityRole.Button, "android.widget.Button")]
    [InlineData(AccessibilityRole.CheckBox, "android.widget.CheckBox")]
    [InlineData(AccessibilityRole.MenuItemCheckBox, "android.widget.CheckBox")]
    [InlineData(AccessibilityRole.Switch, "android.widget.Switch")]
    [InlineData(AccessibilityRole.Radio, "android.widget.RadioButton")]
    [InlineData(AccessibilityRole.TextBox, "android.widget.EditText")]
    [InlineData(AccessibilityRole.SearchBox, "android.widget.EditText")]
    [InlineData(AccessibilityRole.ComboBox, "android.widget.Spinner")]
    [InlineData(AccessibilityRole.Slider, "android.widget.SeekBar")]
    [InlineData(AccessibilityRole.ProgressBar, "android.widget.ProgressBar")]
    [InlineData(AccessibilityRole.Meter, "android.widget.ProgressBar")]
    [InlineData(AccessibilityRole.Image, "android.widget.ImageView")]
    [InlineData(AccessibilityRole.List, "android.widget.ListView")]
    [InlineData(AccessibilityRole.ListBox, "android.widget.ListView")]
    [InlineData(AccessibilityRole.Table, "android.widget.GridView")]
    [InlineData(AccessibilityRole.Heading, "android.widget.TextView")]
    [InlineData(AccessibilityRole.Link, "android.widget.TextView")]
    [InlineData(AccessibilityRole.Paragraph, "android.widget.TextView")]
    [InlineData(AccessibilityRole.Cell, "android.widget.TextView")]
    [InlineData(AccessibilityRole.Navigation, "android.view.View")]
    [InlineData(AccessibilityRole.Group, "android.view.View")]
    [InlineData(AccessibilityRole.Figure, "android.view.View")]
    public void ARoleIsToldAsTheWidgetItIsLike(AccessibilityRole role, string expected)
    {
        Assert.Equal(expected, AccessibilityMapping.ClassName(role));
    }

    [Theory]
    [InlineData(AccessibilityRole.CheckBox, true)]
    [InlineData(AccessibilityRole.Radio, true)]
    [InlineData(AccessibilityRole.Switch, true)]
    [InlineData(AccessibilityRole.MenuItemRadio, true)]
    [InlineData(AccessibilityRole.Button, false)]
    [InlineData(AccessibilityRole.Link, false)]
    public void OnlyThingsThatSwitchHaveACheckedState(AccessibilityRole role, bool expected)
    {
        Assert.Equal(expected, AccessibilityMapping.IsCheckable(role));
    }

    [Theory]
    [InlineData(AccessibilityRole.Link, true)]
    [InlineData(AccessibilityRole.Button, true)]
    [InlineData(AccessibilityRole.CheckBox, true)]
    [InlineData(AccessibilityRole.Tab, true)]
    [InlineData(AccessibilityRole.Paragraph, false)]
    [InlineData(AccessibilityRole.Heading, false)]
    public void OnlyThingsThatCanBeTappedAreClickable(AccessibilityRole role, bool expected)
    {
        Assert.Equal(expected, AccessibilityMapping.IsClickable(role));
    }

    [Theory]
    [InlineData(LiveSetting.Off, 0)]
    [InlineData(LiveSetting.Polite, 1)]
    [InlineData(LiveSetting.Assertive, 2)]
    public void ALiveSettingIsTheValueAndroidUses(LiveSetting live, int expected)
    {
        Assert.Equal(expected, AccessibilityMapping.LiveRegion(live));
    }
}
