using PeachDrawing.Text.Internal.Hinting;
using PeachDrawing.Text.Internal.Hinting.FreeType;
using System.Buffers.Binary;

namespace PeachDrawing.Text.Tests.Hinting
{
    /// <summary>
    /// The CFF2 parts of the DICT parser and of the variation store on their own: the arithmetic of a blend worked out by hand, and the operators
    /// used where they are not allowed. (Against FreeType, and with whole fonts, they are in <see cref="HintingCff2GoldenTests"/> and
    /// <see cref="HintingCff2VariantTests"/>.)
    /// </summary>
    public class HintingCff2ParserTests
    {
        private static byte[] Int(int value) => value is >= -107 and <= 107 ? [(byte)(value + 139)] : [29, .. BitConverter.GetBytes(value).Reverse()];

        private static byte[] Dict(params byte[][] parts) => parts.SelectMany(p => p).ToArray();

        private const byte Blend = 23, VsIndex = 22, StdHw = 10;

        /// <summary>A variation store of one axis with the regions and data sets given (a region is (start, peak, end) in 2.14), as a table with the store at offset 2.</summary>
        private static (byte[] Data, CffVStore Store) Store((short Start, short Peak, short End)[] regions, int[][] dataSets)
        {
            var region = new List<byte>();
            region.AddRange(U16(1));
            region.AddRange(U16(regions.Length));
            foreach (var (start, peak, end) in regions)
            {
                region.AddRange(U16(start));
                region.AddRange(U16(peak));
                region.AddRange(U16(end));
            }

            int header = 8 + 4 * dataSets.Length;
            var ivs = new List<byte>();
            ivs.AddRange(U16(1));
            ivs.AddRange(U32(header));
            ivs.AddRange(U16(dataSets.Length));

            var datas = new List<byte>();
            foreach (var set in dataSets)
            {
                ivs.AddRange(U32(header + region.Count + datas.Count));
                datas.AddRange(U16(0));
                datas.AddRange(U16(0));
                datas.AddRange(U16(set.Length));
                foreach (int index in set)
                    datas.AddRange(U16(index));
            }

            var table = new List<byte> { 0, 0 };
            table.AddRange(U16(ivs.Count + region.Count + datas.Count));
            table.AddRange(ivs);
            table.AddRange(region);
            table.AddRange(datas);

            var data = table.ToArray();
            return (data, CffVStore.Load(data, 0, 2));
        }

        private static byte[] U16(int value) { var b = new byte[2]; BinaryPrimitives.WriteUInt16BigEndian(b, (ushort)value); return b; }

        private static byte[] U32(int value) { var b = new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(b, (uint)value); return b; }

        private static CffBlendContext ContextOf(CffVStore store, params int[] ndv)
        {
            var blend = new CffBlend { VStore = store, HasFont = true };
            return new CffBlendContext(blend, ndv.Length, ndv.Length == 0 ? null : ndv);
        }

        [Fact]
        public void ABlendInAPrivateDictGivesTheDefaultPlusTheDeltasScaledByTheRegions()
        {
            // one region of weight, up to the top of the axis: at half way a delta counts half; StdHW = 68 + 10 * 0.5
            var (_, store) = Store([(0, 0x4000, 0x4000)], [[0]]);
            var dict = Dict(Int(68), Int(10), Int(1), [Blend], [StdHw]);

            var half = new CffPrivate();
            CffParser.Run(dict, 0, dict.Length, CffParser.Kind.Cff2Private, null, half, ContextOf(store, 0x8000));
            Assert.Equal(73, half.StandardWidth);

            var top = new CffPrivate();
            CffParser.Run(dict, 0, dict.Length, CffParser.Kind.Cff2Private, null, top, ContextOf(store, 0x10000));
            Assert.Equal(78, top.StandardWidth);

            // no location at all is the default design: the deltas do not count
            var none = new CffPrivate();
            CffParser.Run(dict, 0, dict.Length, CffParser.Kind.Cff2Private, null, none, ContextOf(store));
            Assert.Equal(68, none.StandardWidth);
        }

        [Fact]
        public void AVsindexChoosesTheDataSetABlendUses()
        {
            // data set 0 names the region of the first axis position, data set 1 nothing: with vsindex 1 a blend of one value is the value itself
            var (_, store) = Store([(0, 0x4000, 0x4000)], [[0], []]);
            var dict = Dict(Int(1), [VsIndex], Int(68), Int(1), [Blend], [StdHw]);

            var priv = new CffPrivate();
            CffParser.Run(dict, 0, dict.Length, CffParser.Kind.Cff2Private, null, priv, ContextOf(store, 0x10000));

            Assert.Equal(1u, priv.VsIndex);
            Assert.Equal(68, priv.StandardWidth);
        }

        [Fact]
        public void AVsindexAfterABlendIsRefused()
        {
            var (_, store) = Store([(0, 0x4000, 0x4000)], [[0]]);
            var dict = Dict(Int(68), Int(10), Int(1), [Blend], [StdHw], Int(0), [VsIndex]);

            // a DICT that uses a blend and then names its data set is refused
            Assert.Throws<HintingException>(() => CffParser.Run(dict, 0, dict.Length, CffParser.Kind.Cff2Private, null, new CffPrivate(), ContextOf(store, 0x8000)));
        }

        [Fact]
        public void ABlendOrAVsindexWhereThereIsNoContextIsRefused()
        {
            var blend = Dict(Int(68), Int(10), Int(1), [Blend]);
            Assert.Throws<HintingException>(() => CffParser.Run(blend, 0, blend.Length, CffParser.Kind.Cff2Private, null, new CffPrivate(), null));

            var vsindex = Dict(Int(1), [VsIndex]);
            Assert.Throws<HintingException>(() => CffParser.Run(vsindex, 0, vsindex.Length, CffParser.Kind.Cff2Private, null, new CffPrivate(), null));
        }

        [Fact]
        public void ABlendWithNothingToBlendIsRefused()
        {
            var (_, store) = Store([(0, 0x4000, 0x4000)], [[0]]);

            var noOperands = Dict([Blend]);
            Assert.Throws<HintingException>(() => CffParser.Run(noOperands, 0, noOperands.Length, CffParser.Kind.Cff2Private, null, new CffPrivate(), ContextOf(store)));

            // one value and its delta are two operands, and the number of values is a third
            var tooFew = Dict(Int(68), Int(1), [Blend]);
            Assert.Throws<HintingException>(() => CffParser.Run(tooFew, 0, tooFew.Length, CffParser.Kind.Cff2Private, null, new CffPrivate(), ContextOf(store)));

            // a vsindex that is not one of the data sets
            var badIndex = Dict(Int(5), [VsIndex], Int(68), Int(10), Int(1), [Blend]);
            Assert.Throws<HintingException>(() => CffParser.Run(badIndex, 0, badIndex.Length, CffParser.Kind.Cff2Private, null, new CffPrivate(), ContextOf(store)));
        }

        [Fact]
        public void AChainOfBlendsThatWouldFillMemoryIsRefusedInBoundedTimeAndSpace()
        {
            // A blend leaves its results on the stack, so `512 blend` can be repeated for the price of two bytes; each makes 2,560 bytes of
            // results when the data set has no regions. A Private DICT of half a megabyte would ask for gigabytes.
            var (_, store) = Store([(0, 0x4000, 0x4000)], [[]]);
            var parts = new List<byte[]>();
            for (int i = 0; i < 512; i++)
                parts.Add(Int(1));
            for (int i = 0; i < 100_000; i++)
            {
                parts.Add(Int(512));
                parts.Add([Blend]);
            }

            var dict = Dict([.. parts]);
            long before = GC.GetAllocatedBytesForCurrentThread();

            WorkBounds.Case(() => Assert.Throws<HintingException>(() => CffParser.Run(dict, 0, dict.Length, CffParser.Kind.Cff2Private, null, new CffPrivate(), ContextOf(store))));

            Assert.True(GC.GetAllocatedBytesForCurrentThread() - before < 16 * 1024 * 1024, "allocated more than 16 MB");
        }

        [Fact]
        public void AStoreThatIsCutOffOrPointsOutsideTheFontIsRefused()
        {
            var (data, _) = Store([(0, 0x4000, 0x4000)], [[0]]);

            Assert.Throws<HintingException>(() => CffVStore.Load(data[..(data.Length - 1)], 0, 2));
            Assert.Throws<HintingException>(() => CffVStore.Load(data, 0, (uint)data.Length));
            Assert.Throws<HintingException>(() => CffVStore.Load(data, 0, 0xFFFF_FFF0u));
            Assert.Same(CffVStore.Empty, CffVStore.Load(data, 0, 0));
        }

        [Fact]
        public void ABlendVectorDoesNotNeedRebuildingForTheSameDataSetAndLocation()
        {
            var (_, store) = Store([(0, 0x4000, 0x4000)], [[0]]);
            var blend = new CffBlend { VStore = store, HasFont = true };
            int[] ndv = [0x8000];

            Assert.True(blend.CheckVector(0, 1, ndv));
            Assert.False(blend.BuildVector(0, 1, ndv));
            Assert.False(blend.CheckVector(0, 1, ndv));
            Assert.Equal([0x10000, 0x8000], blend.BV);

            // another data set, another number of coordinates, another location
            Assert.True(blend.CheckVector(1, 1, ndv));
            Assert.True(blend.CheckVector(0, 0, null));
            Assert.True(blend.CheckVector(0, 1, [0x9000]));

            // a data set that is not there, a vector of the wrong length, no vector where one is needed
            Assert.True(blend.BuildVector(3, 1, ndv));
            Assert.True(blend.BuildVector(0, 2, [0, 0]));
            Assert.True(blend.BuildVector(0, 1, null));
            Assert.True(blend.CheckVector(0, 1, ndv));
        }
    }
}
