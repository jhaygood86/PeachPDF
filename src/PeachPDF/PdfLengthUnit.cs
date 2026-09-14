namespace PeachPDF
{
    /// <summary>
    /// The subset of CSS <c>&lt;length&gt;</c>/<c>&lt;percentage&gt;</c> units meaningful for a
    /// declaratively-built print document (see <see cref="PdfLength"/>). Viewport- and
    /// container-query-relative units (<c>vw</c>, <c>cqw</c>, etc.) are deliberately not exposed here -
    /// they resolve against a browser viewport/query container, neither of which a declarative
    /// document-building call has one of.
    /// </summary>
    public enum PdfLengthUnit
    {
        /// <summary>1/72 inch - PeachPDF's own internal layout unit.</summary>
        Point,

        /// <summary>CSS pixels, 1px = 1/96in = 0.75pt.</summary>
        Pixel,

        /// <summary>Inches.</summary>
        Inch,

        /// <summary>Centimeters.</summary>
        Centimeter,

        /// <summary>Millimeters.</summary>
        Millimeter,

        /// <summary>Picas (1pc = 12pt).</summary>
        Pica,

        /// <summary>A percentage of whatever basis the property being set resolves percentages against.</summary>
        Percent,

        /// <summary>Relative to the current element's own font size.</summary>
        Em,

        /// <summary>Relative to the document root's font size.</summary>
        Rem
    }
}
