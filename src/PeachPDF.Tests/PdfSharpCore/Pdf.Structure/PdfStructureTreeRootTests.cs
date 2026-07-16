using PeachPDF.PdfSharpCore.Pdf;
using PeachPDF.PdfSharpCore.Pdf.Structure;
using System.Linq;

namespace PeachPDF.Tests.PdfSharpCoreTests.Pdf.Structure
{
    public class PdfStructureTreeRootTests
    {
        [Fact]
        public void Constructor_SetsTypeToStructTreeRoot()
        {
            var doc = new PdfDocument();
            var root = new PdfStructureTreeRoot(doc);

            Assert.Equal("/StructTreeRoot", root.Elements.GetName(PdfStructureTreeRoot.Keys.Type));
        }

        [Fact]
        public void SetRootKid_IsReachableViaPrepareForSave()
        {
            var doc = new PdfDocument();
            var root = new PdfStructureTreeRoot(doc);
            doc.Internals.AddObject(root);

            var documentElement = new PdfStructureElement(doc) { StructureType = "/Document" };
            doc.Internals.AddObject(documentElement);
            documentElement.Parent = root;

            root.SetRootKid(documentElement);
            root.PrepareForSave();

            var kids = PdfStructureElement.GetKids(root.Elements).ToList();
            Assert.Single(kids);
            Assert.Same(documentElement, kids[0]);
        }

        [Fact]
        public void ParentTree_LazilyCreatesIndirectNumberTreeNode()
        {
            var doc = new PdfDocument();
            var root = new PdfStructureTreeRoot(doc);
            doc.Internals.AddObject(root);

            var parentTree = root.ParentTree;

            Assert.NotNull(parentTree);
            Assert.True(parentTree.IsIndirect);
            Assert.Same(parentTree, root.ParentTree);
        }

        [Fact]
        public void ParentTreeNextKey_RoundTrips()
        {
            var doc = new PdfDocument();
            var root = new PdfStructureTreeRoot(doc);
            doc.Internals.AddObject(root);

            root.ParentTreeNextKey = 3;

            Assert.Equal(3, root.ParentTreeNextKey);
        }
    }
}
