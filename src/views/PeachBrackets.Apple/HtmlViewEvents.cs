using PeachBrackets;

namespace PeachBrackets.Apple;

/// <summary>Raised when a link in an <see cref="HtmlView"/> is tapped or clicked.</summary>
public sealed class HtmlFormSubmittingEventArgs : EventArgs
{
    internal HtmlFormSubmittingEventArgs(PeachBrackets.Updates.FormSubmission submission) => Submission = submission;

    /// <summary>The form as it is submitted: where to, how, and what each control holds. The view does not send it; <see cref="PeachBrackets.Updates.FormSubmission.Serialize"/> makes the body.</summary>
    public PeachBrackets.Updates.FormSubmission Submission { get; }
}

/// <summary>The form controls whose values do not satisfy their constraints when a submit was tried (<c>HtmlView.FormInvalid</c>).</summary>
public sealed class HtmlFormInvalidEventArgs : EventArgs
{
    internal HtmlFormInvalidEventArgs(IReadOnlyList<PeachBrackets.Updates.ValidationProblem> problems) => Problems = problems;

    /// <summary>What is wrong, in document order, at most one for each control; the first control has been given the focus.</summary>
    public IReadOnlyList<PeachBrackets.Updates.ValidationProblem> Problems { get; }
}

/// <summary>What a tap on a link is about to do.</summary>
public sealed class HtmlLinkActivatedEventArgs : EventArgs
{
    internal HtmlLinkActivatedEventArgs(string? uri, string? destination)
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

/// <summary>
/// Raised when a touch begins on an <see cref="HtmlView"/>, once the document has said which gestures it allows there. The application can
/// take gestures away (to handle them itself) by clearing flags in <see cref="Allowed"/>; it cannot give back what the document refused.
/// </summary>
public sealed class HtmlTouchStartedEventArgs : EventArgs
{
    internal HtmlTouchStartedEventArgs(int pageIndex, double x, double y, DocumentInteraction interaction)
    {
        PageIndex = pageIndex;
        X = x;
        Y = y;
        Interaction = interaction;
        Allowed = interaction.TouchActions;
    }

    /// <summary>The page the touch is on, or -1 when it is not on a page.</summary>
    public int PageIndex { get; }

    /// <summary>The distance from the left edge of the page, in points.</summary>
    public double X { get; }

    /// <summary>The distance from the top edge of the page, in points.</summary>
    public double Y { get; }

    /// <summary>What the document says about the point: whether it is inert, its <c>touch-action</c>, and whether its text may be selected.</summary>
    public DocumentInteraction Interaction { get; }

    /// <summary>The gestures the view will recognise for this touch. It starts as the document's own <c>touch-action</c>.</summary>
    public TouchActions Allowed { get; set; }
}

/// <summary>Raised when the page is clicked, tapped or long-pressed.</summary>
public sealed class HtmlTappedEventArgs : EventArgs
{
    internal HtmlTappedEventArgs(int pageIndex, double x, double y)
    {
        PageIndex = pageIndex;
        X = x;
        Y = y;
    }

    /// <summary>The page, or -1 when the point is not on a page.</summary>
    public int PageIndex { get; }

    /// <summary>The distance from the left edge of the page, in points.</summary>
    public double X { get; }

    /// <summary>The distance from the top edge of the page, in points.</summary>
    public double Y { get; }
}

/// <summary>The events <c>HtmlView.AnimationEvents</c> reports.</summary>
public sealed class HtmlAnimationEventsEventArgs : EventArgs
{
    internal HtmlAnimationEventsEventArgs(IReadOnlyList<PeachBrackets.Updates.AnimationEventInfo> events) => Events = events;

    /// <summary>The events (<c>animationstart</c>, <c>transitionend</c> and the rest), in the order they happened.</summary>
    public IReadOnlyList<PeachBrackets.Updates.AnimationEventInfo> Events { get; }
}
