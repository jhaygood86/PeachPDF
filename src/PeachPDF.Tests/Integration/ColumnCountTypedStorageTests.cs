using PeachPDF.CSS;
using PeachPDF.Tests.TestSupport;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Direct typed-storage assertions for <c>column-count</c>, now backed by
    /// <c>CssProperty&lt;CssKeywordOrValue&lt;AutoKeyword, int&gt;&gt;</c> - the second property (after the
    /// <c>z-index</c> pilot) through this "group B" keyword-or-value conversion mechanism. Also exercises
    /// the new generator-level <c>min</c> support on a <c>keyword-or-value</c> entry (CSS Multi-column
    /// Layout 1 requires <c>column-count</c>'s <c>&lt;integer&gt;</c> to be &gt;= 1), complementing
    /// <c>MulticolLayoutIntegrationTests</c>, which exercises <c>column-count</c> indirectly through its
    /// effect on column geometry rather than asserting the stored <c>.Value</c> directly.
    /// </summary>
    public class ColumnCountTypedStorageTests
    {
        [Fact]
        public async Task Auto_StoresTheAutoKeyword_CaseInsensitively()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='t' style='column-count:AUTO'></div>"));

            var box = LayoutHarness.FindById(root, "t");

            Assert.NotNull(box);
            Assert.True(box!.ColumnCount.Value is { IsKeyword: true, Keyword: AutoKeyword.Auto });
        }

        [Fact]
        public async Task PositiveInteger_StoresTheParsedCount()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='t' style='column-count:3'></div>"));

            var box = LayoutHarness.FindById(root, "t");

            Assert.NotNull(box);
            Assert.Equal("3", box!.ColumnCount.ToString());
            Assert.True(box.ColumnCount.Value is { IsValue: true, Value: 3 });
        }

        [Fact]
        public async Task MinimumLegalCount_One_IsAccepted()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='t' style='column-count:1'></div>"));

            var box = LayoutHarness.FindById(root, "t");

            Assert.NotNull(box);
            Assert.True(box!.ColumnCount.Value is { IsValue: true, Value: 1 });
        }

        [Fact]
        public async Task Zero_IsRejected_InitialAutoValueIsKept()
        {
            // CSS Multi-column Layout 1: column-count's <integer> must be >= 1. An out-of-range
            // declaration is invalid at the box-application layer, so the whole declaration is dropped
            // and the property keeps its initial value (auto) rather than storing 0.
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='t' style='column-count:0'></div>"));

            var box = LayoutHarness.FindById(root, "t");

            Assert.NotNull(box);
            Assert.True(box!.ColumnCount.Value is { IsKeyword: true, Keyword: AutoKeyword.Auto });
        }

        [Fact]
        public async Task Negative_IsRejected_InitialAutoValueIsKept()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='t' style='column-count:-2'></div>"));

            var box = LayoutHarness.FindById(root, "t");

            Assert.NotNull(box);
            Assert.True(box!.ColumnCount.Value is { IsKeyword: true, Keyword: AutoKeyword.Auto });
        }
    }
}
