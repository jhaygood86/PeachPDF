using PeachReader.Core;

namespace PeachReader.Views;

/// <summary>
/// The scrolling view of a document's pages: sheets of paper on a desk, drawn by the platform's own backend (Win2D on Windows). It has no logic of
/// its own: the <see cref="ViewportController"/> it is given decides the layout, the current page, what a click does and what is highlighted, and
/// the platform handler paints that and feeds the controller the size, scroll position and pointer.
/// </summary>
public sealed class PageViewport : View
{
    /// <summary>The controller that drives the view; <see langword="null"/> shows an empty desk.</summary>
    public static readonly BindableProperty ControllerProperty =
        BindableProperty.Create(nameof(Controller), typeof(ViewportController), typeof(PageViewport), null);

    /// <summary>The controller that drives the view; <see langword="null"/> shows an empty desk.</summary>
    public ViewportController? Controller
    {
        get => (ViewportController?)GetValue(ControllerProperty);
        set => SetValue(ControllerProperty, value);
    }

    /// <summary>Raised when the reader asks to copy text and the view has put it on the clipboard.</summary>
    public event EventHandler? TextCopied;

    internal void RaiseTextCopied() => TextCopied?.Invoke(this, EventArgs.Empty);
}
