using PeachPDF.PdfSharpCore.Pdf;

namespace PeachPDF.Tests.PdfSharpCoreTests.Pdf
{
    public class PdfNumberTreeNodeTests
    {
        [Fact]
        public void AddNumber_SingleEntry_IsRetrievable()
        {
            var doc = new PdfDocument();
            var node = new PdfNumberTreeNode();
            doc.Internals.AddObject(node);

            var value = new PdfInteger(42);
            node.AddNumber(5, value);

            Assert.True(node.ContainsNumber(5));
            Assert.Equal(42, ((PdfInteger)node.GetValue(5)!).Value);
        }

        [Fact]
        public void AddNumber_KeepsEntriesSortedByKey()
        {
            var doc = new PdfDocument();
            var node = new PdfNumberTreeNode();
            doc.Internals.AddObject(node);

            node.AddNumber(5, new PdfInteger(1));
            node.AddNumber(1, new PdfInteger(2));
            node.AddNumber(3, new PdfInteger(3));

            var nums = node.Elements.GetArray("/Nums")!;
            Assert.Equal(1, nums.Elements.GetInteger(0));
            Assert.Equal(3, nums.Elements.GetInteger(2));
            Assert.Equal(5, nums.Elements.GetInteger(4));
        }

        [Fact]
        public void ContainsNumber_MissingKey_ReturnsFalse()
        {
            var doc = new PdfDocument();
            var node = new PdfNumberTreeNode();
            doc.Internals.AddObject(node);

            node.AddNumber(1, new PdfInteger(0));

            Assert.False(node.ContainsNumber(99));
        }

        [Fact]
        public void GetValue_ResolvesIndirectReferences()
        {
            var doc = new PdfDocument();
            var node = new PdfNumberTreeNode();
            doc.Internals.AddObject(node);

            var referenced = new PdfDictionary(doc);
            doc.Internals.AddObject(referenced);
            referenced.Elements.SetName("/Type", "/Test");

            node.AddNumber(7, referenced.Reference);

            var resolved = node.GetValue(7);
            Assert.Same(referenced, resolved);
        }

        [Fact]
        public void LeastAndGreatestKey_ReflectAddedEntries()
        {
            var doc = new PdfDocument();
            var node = new PdfNumberTreeNode();
            doc.Internals.AddObject(node);

            node.AddNumber(10, new PdfInteger(0));
            node.AddNumber(2, new PdfInteger(0));
            node.AddNumber(6, new PdfInteger(0));

            Assert.Equal(2, node.LeastKey);
            Assert.Equal(10, node.GreatestKey);
        }

        [Fact]
        public void IsRoot_TrueWhenNoParentAssigned()
        {
            var doc = new PdfDocument();
            var root = new PdfNumberTreeNode();
            doc.Internals.AddObject(root);
            var kid = new PdfNumberTreeNode();
            doc.Internals.AddObject(kid);

            root.AddKid(kid);

            Assert.True(root.IsRoot);
            Assert.False(kid.IsRoot);
            Assert.Same(root, kid.Parent);
        }
    }
}
