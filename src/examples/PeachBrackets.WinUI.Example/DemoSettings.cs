using Microsoft.UI.Xaml;
using PeachDrawing.Win2D;
using Windows.UI;

namespace PeachBrackets.WinUI.Example;

/// <summary>How the host lays the view out: the box the widget sits in.</summary>
internal enum HeightMode
{
    /// <summary>The widget has the height of the slider, and a document taller than it scrolls (or is clipped).</summary>
    Fixed,

    /// <summary>The widget is as tall as its document, which is what a row in a list wants.</summary>
    Content,
}

/// <summary>Which pointing devices the view tells the document it has.</summary>
internal enum InputChoice
{
    Detect,
    Mouse,
    Touch,
    None,
}

/// <summary>
/// Every knob the demo offers a host application, in one place: the panel edits it, and <see cref="Apply"/> is the only code that hands it to a view.
/// A new knob is a property here, a line in <see cref="Apply"/> and a control in the panel.
/// </summary>
internal sealed class DemoSettings
{
    public HtmlViewScrollMode ScrollMode { get; set; } = HtmlViewScrollMode.Auto;

    public bool IsZoomEnabled { get; set; } = true;

    public bool IsLayoutSynchronous { get; set; }

    public Color SelectionColor { get; set; } = Color.FromArgb(255, 173, 214, 255);

    public string UserStyleSheet { get; set; } = string.Empty;

    public double DocumentMargin { get; set; }

    public Color PageBackground { get; set; } = Color.FromArgb(255, 255, 255, 255);

    public Win2dTextRendering TextRendering { get; set; } = Win2dTextRendering.Auto;

    public InputChoice Input { get; set; } = InputChoice.Detect;

    public ElementTheme Theme { get; set; } = ElementTheme.Default;

    public bool PrivateFontSet { get; set; }

    public bool AllowNetworkAccess { get; set; }

    public bool AnimationsEnabled { get; set; } = true;

    /// <summary>Whether the host's box is as wide as the window lets it be (and follows the window as it is resized); off, it is <see cref="HostWidth"/> wide.</summary>
    public bool FillWidth { get; set; } = true;

    public double HostWidth { get; set; } = 700;

    public double HostHeight { get; set; } = 520;

    public HeightMode HeightMode { get; set; } = HeightMode.Fixed;

    public bool ListMode { get; set; }

    /// <summary>The scroll mode that makes sense with the height the host gives: a viewer needs a height to scroll in, a row does not.</summary>
    public HtmlViewScrollMode EffectiveScrollMode => ListMode || HeightMode == HeightMode.Content ? HtmlViewScrollMode.Disabled : ScrollMode;

    public void Apply(HtmlView view, bool row = false)
    {
        view.ScrollMode = row ? HtmlViewScrollMode.Disabled : EffectiveScrollMode;
        view.IsZoomEnabled = IsZoomEnabled;
        view.IsLayoutSynchronous = IsLayoutSynchronous;
        view.SelectionColor = SelectionColor;
        view.UserStyleSheet = UserStyleSheet;
        view.DocumentMargin = DocumentMargin;
        view.PageBackground = PageBackground;
        view.TextRendering = TextRendering;
        view.AllowNetworkAccess = AllowNetworkAccess;
        view.AnimationsEnabled = AnimationsEnabled;
        view.RequestedTheme = Theme;
        view.Input = Input switch
        {
            InputChoice.Mouse => InputCapabilities.Mouse,
            InputChoice.Touch => InputCapabilities.Touch,
            InputChoice.None => InputCapabilities.None,
            _ => null,
        };
    }
}
