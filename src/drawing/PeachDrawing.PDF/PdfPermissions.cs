#nullable enable

namespace PeachDrawing.PDF
{
    /// <summary>
    /// The operations a PDF reader may allow a person who opened an encrypted document with its
    /// <em>user</em> password. A person who opens it with the <em>owner</em> password is never restricted.
    /// A reader enforces these voluntarily: they are a request made of well-behaved software, not a
    /// cryptographic barrier, because anyone who can open the document can read its content.
    /// </summary>
    /// <remarks>
    /// Each value is the corresponding bit of the PDF permissions word (ISO 32000-2, Table 22), so the
    /// flags combine freely. Start from <see cref="All"/> and clear what to forbid, or from
    /// <see cref="None"/> and add what to allow.
    /// </remarks>
    [System.Flags]
    public enum PdfPermissions
    {
        /// <summary>Nothing is allowed beyond opening and viewing the document.</summary>
        None = 0,

        /// <summary>Print the document (at low quality unless <see cref="AllowHighQualityPrint"/> is also set).</summary>
        AllowPrint = 1 << 2,

        /// <summary>Modify the content of the document by operations other than those covered by the other flags.</summary>
        AllowModify = 1 << 3,

        /// <summary>Copy or extract text and graphics from the document (the copy/paste permission).</summary>
        AllowCopy = 1 << 4,

        /// <summary>Add or modify text annotations and, together with <see cref="AllowFillForms"/>, interactive form fields.</summary>
        AllowAnnotations = 1 << 5,

        /// <summary>Fill in existing interactive form fields, including signature fields.</summary>
        AllowFillForms = 1 << 8,

        /// <summary>Extract text and graphics to support accessibility (screen readers).</summary>
        AllowAccessibilityExtract = 1 << 9,

        /// <summary>Assemble the document: insert, rotate or delete pages and create bookmarks or thumbnails.</summary>
        AllowAssemble = 1 << 10,

        /// <summary>Print a faithful, full-resolution copy. Only meaningful together with <see cref="AllowPrint"/>.</summary>
        AllowHighQualityPrint = 1 << 11,

        /// <summary>
        /// A read-only document: it can be viewed, printed, copied from and read by assistive technology,
        /// but not modified, annotated, filled in or reassembled.
        /// </summary>
        ReadOnly = AllowPrint | AllowHighQualityPrint | AllowCopy | AllowAccessibilityExtract,

        /// <summary>Every operation is allowed.</summary>
        All = AllowPrint | AllowModify | AllowCopy | AllowAnnotations | AllowFillForms
            | AllowAccessibilityExtract | AllowAssemble | AllowHighQualityPrint,
    }
}
