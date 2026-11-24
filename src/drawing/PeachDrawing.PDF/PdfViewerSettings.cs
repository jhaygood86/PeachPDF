namespace PeachDrawing.PDF;

/// <summary>What a viewer shows beside the pages when it opens the document.</summary>
public enum PdfPageMode
{
    /// <summary>Only the pages. (A document that has bookmarks and sets no mode is opened with them shown.)</summary>
    UseNone,

    /// <summary>The bookmarks (the document outline) beside the pages.</summary>
    UseOutlines,

    /// <summary>Page thumbnails beside the pages.</summary>
    UseThumbs,

    /// <summary>Full-screen, with no menu bar, window controls or other windows.</summary>
    FullScreen,

    /// <summary>The attachments panel beside the pages.</summary>
    UseAttachments,
}

/// <summary>How a viewer lays the pages out when it opens the document.</summary>
public enum PdfPageLayout
{
    /// <summary>One page at a time.</summary>
    SinglePage,

    /// <summary>The pages in one continuous column.</summary>
    OneColumn,

    /// <summary>The pages in two continuous columns, odd-numbered pages on the left.</summary>
    TwoColumnLeft,

    /// <summary>The pages in two continuous columns, odd-numbered pages on the right.</summary>
    TwoColumnRight,

    /// <summary>Two pages at a time, odd-numbered pages on the left.</summary>
    TwoPageLeft,

    /// <summary>Two pages at a time, odd-numbered pages on the right.</summary>
    TwoPageRight,
}

/// <summary>What a viewer does as it opens the document, besides showing the first page.</summary>
public enum PdfOpenAction
{
    /// <summary>Shows the first page scaled to fit the window.</summary>
    FitPage,

    /// <summary>Shows the first page scaled to the window's width.</summary>
    FitWidth,

    /// <summary>Opens the viewer's print dialog. Not allowed in a PDF/A or PDF/X file, which may not carry actions that run.</summary>
    Print,
}

/// <summary>How a viewer's print dialog scales the pages.</summary>
public enum PdfPrintScaling
{
    /// <summary>The viewer's own default (usually shrink to the printable area).</summary>
    AppDefault,

    /// <summary>No scaling: the pages print at their own size.</summary>
    None,
}

/// <summary>The sides a viewer's print dialog should print on.</summary>
public enum PdfDuplex
{
    /// <summary>One side of each sheet.</summary>
    Simplex,

    /// <summary>Both sides, flipped on the short edge.</summary>
    FlipShortEdge,

    /// <summary>Both sides, flipped on the long edge.</summary>
    FlipLongEdge,
}

/// <summary>Preferences for how a viewer shows the document and offers to print it (the catalog's viewer preferences). A viewer may ignore them.</summary>
public sealed class PdfViewerOptions
{
    /// <summary>Show the document title, not the file name, in the window's title bar. Always set for a PDF/UA file.</summary>
    public bool DisplayDocTitle { get; set; }

    /// <summary>Hide the viewer's toolbars.</summary>
    public bool HideToolbar { get; set; }

    /// <summary>Hide the viewer's menu bar.</summary>
    public bool HideMenubar { get; set; }

    /// <summary>Hide the viewer's window controls (scroll bars, navigation panels), leaving the pages.</summary>
    public bool HideWindowUI { get; set; }

    /// <summary>Resize the viewer's window to fit the first page.</summary>
    public bool FitWindow { get; set; }

    /// <summary>Centre the viewer's window on the screen.</summary>
    public bool CenterWindow { get; set; }

    /// <summary>How the print dialog scales the pages; null leaves it to the viewer.</summary>
    public PdfPrintScaling? PrintScaling { get; set; }

    /// <summary>The sides the print dialog prints on; null leaves it to the viewer.</summary>
    public PdfDuplex? Duplex { get; set; }
}
