using System.Collections.Generic;
using PeachPDF.Html.Core;

namespace PeachPDF.Tests.Html.Core
{
    /// <summary>
    /// Direct coverage for <see cref="ContainerQuerySizes.SizesEqual"/>'s per-field comparison - in
    /// particular that <see cref="ContainerQueryContext.WidthPt"/>/<see cref="ContainerQueryContext.HeightPt"/>
    /// are compared independently of <see cref="ContainerQueryContext.InlineSizePt"/>/
    /// <see cref="ContainerQueryContext.BlockSizePt"/> (issue #806's own review pass), not just whichever
    /// pair happens to be exercised end-to-end by the convergence-loop integration tests.
    /// </summary>
    public class ContainerQuerySizesTests
    {
        private static ContainerQuerySizes Build(ContainerQueryContext context) =>
            new(new Dictionary<uint, ContainerQueryContext> { [1] = context });

        [Fact]
        public void SizesEqual_IdenticalContexts_ReturnsTrue()
        {
            var a = Build(new ContainerQueryContext(400d, 200d, 400d, 200d, "none"));
            var b = Build(new ContainerQueryContext(400d, 200d, 400d, 200d, "none"));

            Assert.True(a.SizesEqual(b));
        }

        [Fact]
        public void SizesEqual_DifferentWidthPt_ReturnsFalse()
        {
            var a = Build(new ContainerQueryContext(400d, 200d, 400d, 200d, "none"));
            var b = Build(new ContainerQueryContext(500d, 200d, 400d, 200d, "none"));

            Assert.False(a.SizesEqual(b));
        }

        [Fact]
        public void SizesEqual_DifferentHeightPt_ReturnsFalse()
        {
            var a = Build(new ContainerQueryContext(400d, 200d, 400d, 200d, "none"));
            var b = Build(new ContainerQueryContext(400d, 300d, 400d, 200d, "none"));

            Assert.False(a.SizesEqual(b));
        }

        [Fact]
        public void SizesEqual_OneSideHasNullHeightPtAndTheOtherDoesnt_ReturnsFalse()
        {
            // The inline-size-only <-> size container-type transition case: HeightPt goes from null to a
            // real value (or vice versa) between passes - a real, size-relevant change the convergence
            // loop must not treat as converged.
            var a = Build(new ContainerQueryContext(400d, null, 400d, null, "none"));
            var b = Build(new ContainerQueryContext(400d, 200d, 400d, 200d, "none"));

            Assert.False(a.SizesEqual(b));
            Assert.False(b.SizesEqual(a));
        }

        [Fact]
        public void SizesEqual_BothSidesHaveNullHeightPt_ReturnsTrue()
        {
            var a = Build(new ContainerQueryContext(400d, null, 400d, null, "none"));
            var b = Build(new ContainerQueryContext(400d, null, 400d, null, "none"));

            Assert.True(a.SizesEqual(b));
        }

        [Fact]
        public void SizesEqual_DifferentInlineSizePt_ReturnsFalse()
        {
            var a = Build(new ContainerQueryContext(400d, 200d, 400d, 200d, "none"));
            var b = Build(new ContainerQueryContext(400d, 200d, 200d, 400d, "none"));

            Assert.False(a.SizesEqual(b));
        }

        [Fact]
        public void SizesEqual_DifferentBlockSizePt_ReturnsFalse()
        {
            var a = Build(new ContainerQueryContext(400d, 200d, 400d, 200d, "none"));
            var b = Build(new ContainerQueryContext(400d, 200d, 400d, 300d, "none"));

            Assert.False(a.SizesEqual(b));
        }

        [Fact]
        public void SizesEqual_DifferentContainerCount_ReturnsFalse()
        {
            var a = Build(new ContainerQueryContext(400d, 200d, 400d, 200d, "none"));
            var b = new ContainerQuerySizes(new Dictionary<uint, ContainerQueryContext>());

            Assert.False(a.SizesEqual(b));
        }

        [Fact]
        public void SizesEqual_DifferentContainerIds_ReturnsFalse()
        {
            var a = new ContainerQuerySizes(new Dictionary<uint, ContainerQueryContext>
            {
                [1] = new ContainerQueryContext(400d, 200d, 400d, 200d, "none")
            });
            var b = new ContainerQuerySizes(new Dictionary<uint, ContainerQueryContext>
            {
                [2] = new ContainerQueryContext(400d, 200d, 400d, 200d, "none")
            });

            Assert.False(a.SizesEqual(b));
        }
    }
}
