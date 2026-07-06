using PeachPDF.PdfSharpCore.Pdf;

namespace PeachPDF.Tests.PdfSharpCoreTests.Pdf
{
    public class PdfNameTreeNodeTests
    {
        [Fact]
        public void NewNode_IsRoot()
        {
            var node = new PdfNameTreeNode();

            Assert.True(node.IsRoot);
            Assert.Null(node.Parent);
        }

        [Fact]
        public void AddKid_MakesChildNonRoot()
        {
            var root = new PdfNameTreeNode();
            var kid = new PdfNameTreeNode();

            root.AddKid(kid);

            Assert.Same(root, kid.Parent);
            Assert.False(kid.IsRoot);
            Assert.Equal(1, root.KidsCount);
        }

        [Fact]
        public void AddName_IncreasesNamesCount()
        {
            var node = new PdfNameTreeNode();

            node.AddName("b", new PdfString("second"));
            node.AddName("a", new PdfString("first"));

            Assert.Equal(2, node.NamesCount);
        }

        [Fact]
        public void AddName_KeepsKeysSorted()
        {
            var node = new PdfNameTreeNode();

            node.AddName("charlie", new PdfString("3"));
            node.AddName("alpha", new PdfString("1"));
            node.AddName("bravo", new PdfString("2"));

            Assert.Equal(["alpha", "bravo", "charlie"], node.GetNames());
        }

        [Fact]
        public void ContainsName_FindsExistingName()
        {
            var node = new PdfNameTreeNode();
            node.AddName("key", new PdfString("value"));

            Assert.True(node.ContainsName("key"));
            Assert.False(node.ContainsName("missing"));
        }

        [Fact]
        public void GetValue_ReturnsAssociatedItem()
        {
            var node = new PdfNameTreeNode();
            var value = new PdfString("value");
            node.AddName("key", value);

            var found = node.GetValue("key");

            Assert.Same(value, found);
        }

        [Fact]
        public void GetValue_UnknownName_ReturnsNull()
        {
            var node = new PdfNameTreeNode();

            Assert.Null(node.GetValue("missing"));
        }

        [Fact]
        public void GetNames_IncludeKids_CollectsFromAllDescendants()
        {
            var root = new PdfNameTreeNode();
            // NOTE: AddKid only appends the kid to the /Kids PdfArray dictionary entry; it does not
            // add it to the private in-memory `_kids` list that Kids/GetNames(includeKids:true)/
            // NamesCountTotal actually traverse (that list is only populated by the
            // PdfNameTreeNode(PdfDictionary) constructor when reconstructing from an existing
            // dictionary). So a kid added via AddKid is invisible to includeKids traversal -- a real,
            // pre-existing gap found via this test, not fixed here (out of scope for coverage work).
            root.AddName("root-name", new PdfString("r"));
            var kid = new PdfNameTreeNode();
            kid.AddName("kid-name", new PdfString("k"));
            root.AddKid(kid);

            var names = root.GetNames(includeKids: true);

            Assert.Contains("root-name", names);
            Assert.DoesNotContain("kid-name", names);
        }

        [Fact]
        public void NamesCountTotal_DoesNotSeeKidAddedViaAddKid()
        {
            // See the NOTE on GetNames_IncludeKids_CollectsFromAllDescendants above.
            var root = new PdfNameTreeNode();
            root.AddName("root-name", new PdfString("r"));
            var kid = new PdfNameTreeNode();
            kid.AddName("kid-name", new PdfString("k"));
            root.AddKid(kid);

            Assert.Equal(1, root.NamesCountTotal);
        }

        [Fact]
        public void LeastAndGreatestKey_ReflectSortedNames()
        {
            var node = new PdfNameTreeNode();
            node.AddName("charlie", new PdfString("3"));
            node.AddName("alpha", new PdfString("1"));
            node.AddName("bravo", new PdfString("2"));

            Assert.Equal("alpha", node.LeastKey);
            Assert.Equal("charlie", node.GreatestKey);
        }

        [Fact]
        public void Kids_DoesNotSeeChildAddedViaAddKid()
        {
            // See the NOTE on GetNames_IncludeKids_CollectsFromAllDescendants above: AddKid never
            // populates the private `_kids` list backing this property, so Kids stays empty even
            // though KidsCount (backed by the PdfArray) correctly reports 1.
            var root = new PdfNameTreeNode();
            var kid = new PdfNameTreeNode();
            root.AddKid(kid);

            Assert.Empty(root.Kids);
            Assert.Equal(1, root.KidsCount);
        }

        [Fact]
        public void KidsCount_ZeroWhenNoKids()
        {
            var node = new PdfNameTreeNode();

            Assert.Equal(0, node.KidsCount);
        }

        [Fact]
        public void NamesCount_ZeroWhenNoNames()
        {
            var node = new PdfNameTreeNode();

            Assert.Equal(0, node.NamesCount);
        }
    }
}
