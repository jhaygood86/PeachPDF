namespace PeachPDF
{
    /// <summary>
    /// The Factur-X / ZUGFeRD profile of the invoice XML embedded in a hybrid invoice
    /// (<see cref="FacturXOptions"/>) - how much of the EN 16931 invoice data model the XML carries.
    /// The profiles are nested, each containing the one before it.
    /// </summary>
    /// <remarks>
    /// <see cref="Minimum"/> and <see cref="BasicWl"/> do not carry enough to be an invoice under German
    /// tax law (they serve as booking aids), and the German rules forbid them where both seller and buyer
    /// are in Germany; prefer <see cref="Basic"/> or, better, <see cref="En16931"/>.
    /// </remarks>
    public enum FacturXProfile
    {
        /// <summary>The minimum data set. Not an invoice under German fiscal law.</summary>
        Minimum,

        /// <summary>Basic, without invoice lines. Not an invoice under German fiscal law.</summary>
        BasicWl,

        /// <summary>Basic: a compliant subset of EN 16931, with invoice lines.</summary>
        Basic,

        /// <summary>The full EN 16931 core invoice (formerly called COMFORT). The recommended profile.</summary>
        En16931,

        /// <summary>EN 16931 plus extensions for complex business cases.</summary>
        Extended,

        /// <summary>
        /// The German XRechnung reference profile, embedded as <c>xrechnung.xml</c> instead of
        /// <c>factur-x.xml</c>.
        /// </summary>
        XRechnung,
    }
}
