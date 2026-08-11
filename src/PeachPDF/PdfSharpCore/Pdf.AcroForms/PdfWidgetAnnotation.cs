using PeachPDF.PdfSharpCore.Pdf.Annotations;

namespace PeachPDF.PdfSharpCore.Pdf.AcroForms
{
    /// <summary>
    /// A standalone Widget annotation for one radio button in a <see cref="PdfRadioButtonField"/>
    /// group - see that class's own doc comment for why radio buttons are the one case that cannot
    /// merge field and widget into a single object the way <see cref="PdfAcroField"/>'s other
    /// subclasses do.
    /// </summary>
    internal sealed class PdfWidgetAnnotation : PdfAnnotation
    {
        public PdfWidgetAnnotation(PdfDocument document)
            : base(document)
        {
            Elements.SetName(PdfAnnotation.Keys.Subtype, "/Widget");
        }

        /// <summary>The owning <see cref="PdfRadioButtonField"/> ("/Parent") - the field's own /FT, /T and /V live there, not here. Distinct from the inherited annotation-level <see cref="PdfAnnotation.Parent"/> (the page's <see cref="PdfAnnotations"/> collection), an unrelated key.</summary>
        public PdfAcroField FieldParent
        {
            get { return (PdfAcroField)Elements.GetDictionary(PdfAcroField.Keys.Parent); }
            set { Elements.SetReference(PdfAcroField.Keys.Parent, value); }
        }

        /// <summary>This widget's own appearance state ("/AS") - "/Off" or its export value when this is the selected button in the group.</summary>
        public string AppearanceState
        {
            get { return Elements.GetName(PdfAnnotation.Keys.AS); }
            set { Elements.SetName(PdfAnnotation.Keys.AS, value); }
        }

        // No Meta override, matching PdfAnnotation's own base class: it leaves Meta at PdfDictionary's
        // default (null) rather than declaring one, and this type adds no keys of its own beyond
        // PdfAnnotation's - see PdfAcroField's own override for the shape when one is warranted.
    }
}
