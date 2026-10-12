using System;

namespace PeachPDF
{
    /// <summary>
    /// Password protection and permissions for a generated PDF, written with the PDF standard security
    /// handler using AES-256 (revision 6 of ISO 32000-2). Older handlers (RC4, AES-128) depend on
    /// deprecated algorithms and are not offered.
    /// </summary>
    /// <remarks>
    /// Set it on <see cref="PdfGenerateConfig.Encryption"/>. It cannot be combined with PDF/A or PDF/X, both
    /// of which forbid encryption, and needs a PDF 1.7 or 2.0 document (the default is 2.0).
    /// </remarks>
    public sealed class PdfEncryptionOptions
    {
        /// <summary>
        /// The password a reader must supply to open the document. When <see langword="null"/> or empty the document
        /// opens without a password, and <see cref="Permissions"/> still apply.
        /// </summary>
        public string? UserPassword { get; set; }

        /// <summary>
        /// The password that lifts every restriction in <see cref="Permissions"/>. When <see langword="null"/> or empty a random
        /// one is generated and discarded, so nobody can ever lift the restrictions.
        /// </summary>
        public string? OwnerPassword { get; set; }

        /// <summary>The operations allowed to a person who opened the document with the user password. Defaults to <see cref="PdfPermissions.All"/>.</summary>
        public PdfPermissions Permissions { get; set; } = PdfPermissions.All;

        /// <summary>
        /// Whether the document's XMP metadata stream is encrypted too. When <see langword="false"/> it stays readable
        /// to tools that index documents without a password. Defaults to <see langword="true"/>.
        /// </summary>
        public bool EncryptMetadata { get; set; } = true;

        /// <summary>
        /// Creates options for a read-only document: viewable, printable and copyable, but not editable
        /// (see <see cref="PdfPermissions.ReadOnly"/>).
        /// </summary>
        /// <param name="userPassword">The password to open the document, or <see langword="null"/> for none.</param>
        /// <param name="ownerPassword">The password that lifts the restrictions, or <see langword="null"/> to make them permanent.</param>
        public static PdfEncryptionOptions ReadOnly(string? userPassword = null, string? ownerPassword = null)
            => new()
            {
                UserPassword = userPassword,
                OwnerPassword = ownerPassword,
                Permissions = PdfPermissions.ReadOnly,
            };

        /// <summary>Whether two option sets would produce the same protection (used to reject a conflicting second call).</summary>
        internal bool SameAs(PdfEncryptionOptions? other)
            => other is not null
               && UserPassword == other.UserPassword
               && OwnerPassword == other.OwnerPassword
               && Permissions == other.Permissions
               && EncryptMetadata == other.EncryptMetadata;

        /// <summary>A snapshot, so later changes to the caller's instance cannot alter a document being built.</summary>
        internal PdfEncryptionOptions Clone()
            => new()
            {
                UserPassword = UserPassword,
                OwnerPassword = OwnerPassword,
                Permissions = Permissions,
                EncryptMetadata = EncryptMetadata,
            };
    }
}
