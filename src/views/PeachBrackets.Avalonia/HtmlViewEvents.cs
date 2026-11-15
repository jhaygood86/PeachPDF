using PeachBrackets.Updates;

namespace PeachBrackets.Avalonia;

/// <summary>What a click or tap on a link is about to do (<see cref="HtmlView.LinkActivated"/>).</summary>
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

/// <summary>A form about to be submitted (<see cref="HtmlView.FormSubmitting"/>).</summary>
public sealed class HtmlFormSubmittingEventArgs : EventArgs
{
    internal HtmlFormSubmittingEventArgs(FormSubmission submission) => Submission = submission;

    /// <summary>The form as it is submitted: where to, how, and what each control holds. The view does not send it; <see cref="FormSubmission.Serialize"/> makes the body.</summary>
    public FormSubmission Submission { get; }
}

/// <summary>The form controls whose values do not satisfy their constraints when a submit was tried (<see cref="HtmlView.FormInvalid"/>).</summary>
public sealed class HtmlFormInvalidEventArgs : EventArgs
{
    internal HtmlFormInvalidEventArgs(IReadOnlyList<ValidationProblem> problems) => Problems = problems;

    /// <summary>What is wrong, in document order, at most one for each control; the first control has been given the focus.</summary>
    public IReadOnlyList<ValidationProblem> Problems { get; }
}

/// <summary>The animation and transition events of one frame (<see cref="HtmlView.AnimationEvents"/>).</summary>
public sealed class HtmlAnimationEventsEventArgs : EventArgs
{
    internal HtmlAnimationEventsEventArgs(IReadOnlyList<AnimationEventInfo> events) => Events = events;

    /// <summary>The events, in the order the document raised them.</summary>
    public IReadOnlyList<AnimationEventInfo> Events { get; }
}

/// <summary>Raised when the page is clicked or tapped, on something that is not a link or a control (<see cref="HtmlView.PageClicked"/>).</summary>
public sealed class HtmlPageClickedEventArgs : EventArgs
{
    internal HtmlPageClickedEventArgs(int pageIndex, double x, double y)
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
