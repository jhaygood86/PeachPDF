using System.Collections.Generic;

namespace PeachPDF.PdfSharpCore.Pdf.AcroForms
{
    /// <summary>
    /// A radio button group AcroForm field ("/FT /Btn" with the Radio flag, ISO 32000-1 §12.7.4.2.3).
    /// Unlike PeachPDF's other field kinds, this is deliberately non-terminal: PDF requires every
    /// radio button sharing the same HTML <c>name</c> attribute to be a widget-kid of one shared
    /// field object with mutually exclusive "/AS" appearance states, not four independent fields -
    /// so this class does not merge field and widget the way <see cref="PdfAcroField"/>'s other
    /// subclasses do (it declares no "/Subtype"/"/Rect"/"/AP" of its own; those live on each
    /// <see cref="PdfWidgetAnnotation"/> kid instead). It still derives from <see cref="PdfAcroField"/>
    /// for the shared /FT, /T, /Ff, /V storage - the inherited annotation-only members (Rectangle,
    /// AppearanceState, etc.) are simply never set on this object.
    /// </summary>
    internal sealed class PdfRadioButtonField : PdfAcroField
    {
        /// <summary>Bit 16 (ISO 32000-1 Table 227) - marks a button field as a set of radio buttons rather than a checkbox.</summary>
        internal const int RadioFlag = 1 << 15;

        readonly List<PdfWidgetAnnotation> _kids = [];

        public PdfRadioButtonField(PdfDocument document)
            : base(document)
        {
            FieldType = "/Btn";
            FieldFlags = RadioFlag;
        }

        /// <summary>The export value ("/V") of the currently-selected button in the group, or "/Off" when none is checked.</summary>
        public string Value
        {
            get { return Elements.GetName(Keys.V); }
            set { Elements.SetName(Keys.V, value); }
        }

        /// <summary>
        /// Appends a widget as a kid of this field ("/Kids"), setting the kid's own "/Parent" back-reference.
        /// </summary>
        public void AddKid(PdfWidgetAnnotation widget)
        {
            Owner.Internals.AddObject(widget);
            widget.FieldParent = this;
            _kids.Add(widget);

            var array = Elements.GetArray(Keys.Kids);
            if (array == null)
            {
                array = new PdfArray(Owner);
                Elements.SetObject(Keys.Kids, array);
            }
            array.Elements.Add(widget.Reference);
        }

        /// <summary>The widgets appended via <see cref="AddKid"/>, in the order they were added.</summary>
        public IReadOnlyList<PdfWidgetAnnotation> Kids => _kids;
    }
}
