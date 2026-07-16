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

        [Fact]
        public void ContainsNumber_IncludeKids_FindsKeyInChildNode()
        {
            var doc = new PdfDocument();
            var root = new PdfNumberTreeNode();
            doc.Internals.AddObject(root);
            var kid = new PdfNumberTreeNode();
            doc.Internals.AddObject(kid);
            root.AddKid(kid);
            kid.AddNumber(5, new PdfInteger(0));

            Assert.False(root.ContainsNumber(5));
            Assert.True(root.ContainsNumber(5, includeKids: true));
        }

        [Fact]
        public void ContainsNumber_IncludeKids_KeyMissingEverywhere_ReturnsFalse()
        {
            var doc = new PdfDocument();
            var root = new PdfNumberTreeNode();
            doc.Internals.AddObject(root);
            root.AddNumber(1, new PdfInteger(0));
            var kid = new PdfNumberTreeNode();
            doc.Internals.AddObject(kid);
            root.AddKid(kid);
            kid.AddNumber(2, new PdfInteger(0));

            Assert.False(root.ContainsNumber(99, includeKids: true));
        }

        [Fact]
        public void GetValue_IncludeKids_FindsValueInChildNode()
        {
            var doc = new PdfDocument();
            var root = new PdfNumberTreeNode();
            doc.Internals.AddObject(root);
            var kid = new PdfNumberTreeNode();
            doc.Internals.AddObject(kid);
            root.AddKid(kid);
            kid.AddNumber(5, new PdfInteger(42));

            Assert.Null(root.GetValue(5));
            Assert.Equal(42, ((PdfInteger)root.GetValue(5, includeKids: true)!).Value);
        }

        [Fact]
        public void GetValue_IncludeKids_KeyMissingEverywhere_ReturnsNull()
        {
            var doc = new PdfDocument();
            var root = new PdfNumberTreeNode();
            doc.Internals.AddObject(root);
            root.AddNumber(1, new PdfInteger(0));
            var kid = new PdfNumberTreeNode();
            doc.Internals.AddObject(kid);
            root.AddKid(kid);
            kid.AddNumber(2, new PdfInteger(0));

            Assert.Null(root.GetValue(99, includeKids: true));
        }

        [Fact]
        public void DictionaryWrappingConstructor_ParsesExistingKidsArray()
        {
            var doc = new PdfDocument();
            var kidNode = new PdfNumberTreeNode();
            doc.Internals.AddObject(kidNode);
            kidNode.AddNumber(3, new PdfInteger(9));

            var rootDict = new PdfDictionary(doc);
            doc.Internals.AddObject(rootDict);
            var kidsArray = new PdfArray(doc);
            kidsArray.Elements.Add(kidNode);
            rootDict.Elements.SetObject("/Kids", kidsArray);

            var root = new PdfNumberTreeNode(rootDict);
            doc.Internals.AddObject(root);

            var parsedKid = Assert.Single(root.Kids);
            Assert.Same(root, parsedKid.Parent);
            Assert.True(root.ContainsNumber(3, includeKids: true));
        }

        [Fact]
        public void GreatestKey_AggregatesAcrossKids()
        {
            var doc = new PdfDocument();
            var root = new PdfNumberTreeNode();
            doc.Internals.AddObject(root);
            root.AddNumber(1, new PdfInteger(0));

            var kid = new PdfNumberTreeNode();
            doc.Internals.AddObject(kid);
            root.AddKid(kid);
            kid.AddNumber(50, new PdfInteger(0));

            Assert.Equal(50, root.GreatestKey);
        }

        [Fact]
        public void PrepareForSave_UpdatesLimits()
        {
            var doc = new PdfDocument();
            var node = new PdfNumberTreeNode();
            doc.Internals.AddObject(node);
            node.AddNumber(4, new PdfInteger(0));

            node.PrepareForSave();

            var limits = node.Elements.GetArray("/Limits");
            Assert.NotNull(limits);
            Assert.Equal(4, limits!.Elements.GetInteger(0));
            Assert.Equal(4, limits.Elements.GetInteger(1));
        }

        [Fact]
        public void Keys_Meta_IsAccessible()
        {
            Assert.NotNull(new PdfNumberTreeNode().Meta);
        }
    }
}
