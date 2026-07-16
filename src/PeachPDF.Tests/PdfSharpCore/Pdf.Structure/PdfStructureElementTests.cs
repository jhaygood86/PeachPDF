using PeachPDF.PdfSharpCore.Pdf;
using PeachPDF.PdfSharpCore.Pdf.Structure;
using System.Linq;

namespace PeachPDF.Tests.PdfSharpCoreTests.Pdf.Structure
{
    public class PdfStructureElementTests
    {
        [Fact]
        public void Constructor_SetsTypeToStructElem()
        {
            var doc = new PdfDocument();
            var element = new PdfStructureElement(doc);

            Assert.Equal("/StructElem", element.Elements.GetName(PdfStructureElement.Keys.Type));
        }

        [Fact]
        public void ParameterlessConstructor_SetsTypeToStructElem()
        {
            var element = new PdfStructureElement();

            Assert.Equal("/StructElem", element.Elements.GetName(PdfStructureElement.Keys.Type));
        }

        [Fact]
        public void DictionaryWrappingConstructor_SetsTypeToStructElem()
        {
            var doc = new PdfDocument();
            var dict = new PdfDictionary(doc);

            var element = new PdfStructureElement(dict);

            Assert.Equal("/StructElem", element.Elements.GetName(PdfStructureElement.Keys.Type));
        }

        [Fact]
        public void Keys_Meta_IsAccessible()
        {
            Assert.NotNull(new PdfStructureElement().Meta);
        }

        [Fact]
        public void StructureType_RoundTrips()
        {
            var doc = new PdfDocument();
            var element = new PdfStructureElement(doc) { StructureType = "/H1" };

            Assert.Equal("/H1", element.StructureType);
        }

        [Fact]
        public void AlternateText_RoundTrips()
        {
            var doc = new PdfDocument();
            var element = new PdfStructureElement(doc) { AlternateText = "A cat sleeping" };

            Assert.Equal("A cat sleeping", element.AlternateText);
        }

        [Fact]
        public void Parent_RoundTrips_AsReference()
        {
            var doc = new PdfDocument();
            var parent = new PdfStructureElement(doc);
            doc.Internals.AddObject(parent);
            var child = new PdfStructureElement(doc) { Parent = parent };
            doc.Internals.AddObject(child);

            Assert.Same(parent, child.Parent);
        }

        [Fact]
        public void AppendKid_SingleKid_StaysBareAfterPrepareForSave()
        {
            var doc = new PdfDocument();
            var parent = new PdfStructureElement(doc) { StructureType = "/Document" };
            doc.Internals.AddObject(parent);
            var kid = new PdfStructureElement(doc) { StructureType = "/P", Parent = parent };
            doc.Internals.AddObject(kid);

            parent.AppendKid(kid);
            parent.PrepareForSave();

            var kids = PdfStructureElement.GetKids(parent.Elements).ToList();
            Assert.Single(kids);
            Assert.Same(kid, kids[0]);
        }

        [Fact]
        public void AppendKid_MultipleKids_AllReachableViaGetKids()
        {
            var doc = new PdfDocument();
            var parent = new PdfStructureElement(doc) { StructureType = "/L" };
            doc.Internals.AddObject(parent);

            var kid1 = new PdfStructureElement(doc) { StructureType = "/LI", Parent = parent };
            doc.Internals.AddObject(kid1);
            var kid2 = new PdfStructureElement(doc) { StructureType = "/LI", Parent = parent };
            doc.Internals.AddObject(kid2);

            parent.AppendKid(kid1);
            parent.AppendKid(kid2);
            parent.PrepareForSave();

            var kids = PdfStructureElement.GetKids(parent.Elements).ToList();
            Assert.Equal(2, kids.Count);
            Assert.Contains(kid1, kids);
            Assert.Contains(kid2, kids);
        }

        [Fact]
        public void AppendKid_BareMcidInteger_DoesNotAppearInGetKids()
        {
            // GetKids only surfaces dictionary kids (child structure elements) - bare MCID
            // integers are content items, not further structure nodes to recurse into.
            var doc = new PdfDocument();
            var element = new PdfStructureElement(doc) { StructureType = "/P" };
            doc.Internals.AddObject(element);

            element.AppendKid(new PdfInteger(0));

            var kids = PdfStructureElement.GetKids(element.Elements).ToList();
            Assert.Empty(kids);
        }

        [Fact]
        public void AppendKid_WhenKAlreadyHoldsBareSingleItem_WrapsIntoArrayPreservingIt()
        {
            // PrepareForSave's SimplifyKidsArray un-wraps a single-kid "/K" array back to a bare
            // item - AppendKid must still be able to add a further kid afterward (e.g. a struct
            // element that gets more content on a later page), re-wrapping into an array without
            // losing the existing bare item. GetObject (what AppendKid reads the existing bare
            // item back with) only resolves PdfObject-derived items - a single dictionary kid
            // (not a bare MCID integer, which isn't a PdfObject) is what actually exercises it.
            var doc = new PdfDocument();
            var element = new PdfStructureElement(doc) { StructureType = "/P" };
            doc.Internals.AddObject(element);
            var firstKid = new PdfStructureElement(doc) { StructureType = "/Span", Parent = element };
            doc.Internals.AddObject(firstKid);
            element.Elements[PdfStructureElement.Keys.K] = firstKid;

            var secondKid = new PdfStructureElement(doc) { StructureType = "/Span", Parent = element };
            doc.Internals.AddObject(secondKid);
            element.AppendKid(secondKid);

            var array = element.Elements.GetArray(PdfStructureElement.Keys.K);
            Assert.NotNull(array);
            Assert.Equal(2, array!.Elements.Count);
        }

        [Fact]
        public void Page_RoundTrips_AsReference()
        {
            var doc = new PdfDocument();
            var page = doc.AddPage();
            var element = new PdfStructureElement(doc) { Page = page };
            doc.Internals.AddObject(element);

            Assert.Same(page, element.Page);
        }
    }
}
