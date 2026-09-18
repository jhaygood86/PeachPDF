using PeachPDF.PdfSharpCore.Pdf;
using PeachPDF.PdfSharpCore.Pdf.Advanced;
using System.Text;
using Xunit;

namespace PeachPDF.Tests.PdfSharpCoreTests.Pdf
{
    // Regression coverage for a data-integrity bug: ConsolidateImages() used to compute image identity
    // from the content-stream bytes alone (PdfDocument.ImageInfo.XObjectMD5), ignoring the image
    // dictionary's other attributes - most importantly /SMask. Two unrelated images whose base color
    // stream happened to encode to identical bytes, but where only one carried a soft mask (e.g. a PNG
    // alpha channel), would get merged into a single XObject, silently adding or dropping transparency on
    // whichever page lost its own copy. These tests build image XObjects by hand (rather than through the
    // full HTML render pipeline) so the color stream can be held byte-identical while every other
    // attribute is varied independently.
    public class PdfDocumentConsolidateImagesTests
    {
        private static PdfDictionary CreateImageXObject(
            PdfDocument doc,
            byte[] streamBytes,
            string colorSpace = "/DeviceRGB",
            int width = 2,
            int height = 2,
            int bitsPerComponent = 8,
            PdfArray? decode = null,
            PdfDictionary? softMask = null)
        {
            var image = new PdfDictionary(doc);
            image.Elements["/Type"] = new PdfName("/XObject");
            image.Elements["/Subtype"] = new PdfName("/Image");
            image.Elements["/Width"] = new PdfInteger(width);
            image.Elements["/Height"] = new PdfInteger(height);
            image.Elements["/ColorSpace"] = new PdfName(colorSpace);
            image.Elements["/BitsPerComponent"] = new PdfInteger(bitsPerComponent);
            if (decode != null)
                image.Elements["/Decode"] = decode;
            if (softMask != null)
                image.Elements["/SMask"] = softMask.Reference;
            image.CreateStream(streamBytes);

            doc.Internals.AddObject(image);
            return image;
        }

        private static PdfDictionary CreateSoftMask(PdfDocument doc, byte[] streamBytes)
        {
            var mask = new PdfDictionary(doc);
            mask.Elements["/Type"] = new PdfName("/XObject");
            mask.Elements["/Subtype"] = new PdfName("/Image");
            mask.Elements["/Width"] = new PdfInteger(2);
            mask.Elements["/Height"] = new PdfInteger(2);
            mask.Elements["/ColorSpace"] = new PdfName("/DeviceGray");
            mask.Elements["/BitsPerComponent"] = new PdfInteger(8);
            mask.CreateStream(streamBytes);
            doc.Internals.AddObject(mask);
            return mask;
        }

        private static PdfDictionary CreateIccProfileStream(PdfDocument doc, byte[] profileBytes)
        {
            var profile = new PdfDictionary(doc);
            profile.CreateStream(profileBytes);
            doc.Internals.AddObject(profile);
            return profile;
        }

        private static PdfDictionary GetOrCreateXObjectDictionary(PdfDocument doc, PdfPage page)
        {
            var xObjects = page.Resources.Elements.GetDictionary("/XObject");
            if (xObjects != null)
                return xObjects;

            xObjects = new PdfDictionary(doc);
            page.Resources.Elements["/XObject"] = xObjects;
            return xObjects;
        }

        /// <summary>
        /// Registers both images under distinct names on the same page's /XObject resources, runs
        /// ConsolidateImages(), and returns the two PdfDictionary objects the names resolve to afterward -
        /// letting a test assert whether they were merged (same object) or kept apart (different objects).
        /// </summary>
        private static (PdfDictionary First, PdfDictionary Second) ConsolidateAndResolve(
            PdfDocument doc, PdfDictionary image1, PdfDictionary image2)
        {
            var page = doc.AddPage();
            var xObjects = GetOrCreateXObjectDictionary(doc, page);
            xObjects.Elements["/Im1"] = image1.Reference;
            xObjects.Elements["/Im2"] = image2.Reference;

            doc.ConsolidateImages();

            var resolved1 = (PdfDictionary)((PdfReference)xObjects.Elements["/Im1"]).Value;
            var resolved2 = (PdfDictionary)((PdfReference)xObjects.Elements["/Im2"]).Value;
            return (resolved1, resolved2);
        }

        [Fact]
        public void ConsolidateImages_ByteIdenticalImages_AreMerged()
        {
            var doc = new PdfDocument();
            var colorBytes = Encoding.ASCII.GetBytes("identical-color-stream");

            var image1 = CreateImageXObject(doc, colorBytes);
            var image2 = CreateImageXObject(doc, (byte[])colorBytes.Clone());

            var (resolved1, resolved2) = ConsolidateAndResolve(doc, image1, image2);

            Assert.Same(resolved1, resolved2);
        }

        [Fact]
        public void ConsolidateImages_SameColorStreamOnlyOneHasSMask_AreNotMerged()
        {
            var doc = new PdfDocument();
            var colorBytes = Encoding.ASCII.GetBytes("identical-color-stream");
            var mask = CreateSoftMask(doc, Encoding.ASCII.GetBytes("mask-a"));

            var imageWithoutMask = CreateImageXObject(doc, colorBytes);
            var imageWithMask = CreateImageXObject(doc, (byte[])colorBytes.Clone(), softMask: mask);

            var (resolved1, resolved2) = ConsolidateAndResolve(doc, imageWithoutMask, imageWithMask);

            Assert.NotSame(resolved1, resolved2);
            Assert.Same(imageWithoutMask, resolved1);
            Assert.Same(imageWithMask, resolved2);
        }

        [Fact]
        public void ConsolidateImages_SameColorStreamDifferentSMaskContent_AreNotMerged()
        {
            var doc = new PdfDocument();
            var colorBytes = Encoding.ASCII.GetBytes("identical-color-stream");
            var maskA = CreateSoftMask(doc, Encoding.ASCII.GetBytes("mask-a"));
            var maskB = CreateSoftMask(doc, Encoding.ASCII.GetBytes("mask-b"));

            var image1 = CreateImageXObject(doc, colorBytes, softMask: maskA);
            var image2 = CreateImageXObject(doc, (byte[])colorBytes.Clone(), softMask: maskB);

            var (resolved1, resolved2) = ConsolidateAndResolve(doc, image1, image2);

            Assert.NotSame(resolved1, resolved2);
        }

        [Fact]
        public void ConsolidateImages_SameColorStreamSMaskContentIdenticalButDistinctObjects_AreMerged()
        {
            // The two soft masks are separate PdfDictionary instances (different object numbers once
            // indirect) but carry byte-identical content - proving the SMask identity check recurses on
            // content, not on reference/object identity.
            var doc = new PdfDocument();
            var colorBytes = Encoding.ASCII.GetBytes("identical-color-stream");
            var maskBytes = Encoding.ASCII.GetBytes("same-mask-content");
            var mask1 = CreateSoftMask(doc, (byte[])maskBytes.Clone());
            var mask2 = CreateSoftMask(doc, (byte[])maskBytes.Clone());

            var image1 = CreateImageXObject(doc, colorBytes, softMask: mask1);
            var image2 = CreateImageXObject(doc, (byte[])colorBytes.Clone(), softMask: mask2);

            var (resolved1, resolved2) = ConsolidateAndResolve(doc, image1, image2);

            Assert.Same(resolved1, resolved2);
        }

        [Fact]
        public void ConsolidateImages_SameColorStreamDifferentColorSpace_AreNotMerged()
        {
            var doc = new PdfDocument();
            var colorBytes = Encoding.ASCII.GetBytes("identical-color-stream");

            var image1 = CreateImageXObject(doc, colorBytes, colorSpace: "/DeviceRGB");
            var image2 = CreateImageXObject(doc, (byte[])colorBytes.Clone(), colorSpace: "/DeviceGray");

            var (resolved1, resolved2) = ConsolidateAndResolve(doc, image1, image2);

            Assert.NotSame(resolved1, resolved2);
        }

        [Fact]
        public void ConsolidateImages_SameColorStreamDifferentWidth_AreNotMerged()
        {
            var doc = new PdfDocument();
            var colorBytes = Encoding.ASCII.GetBytes("identical-color-stream");

            var image1 = CreateImageXObject(doc, colorBytes, width: 2);
            var image2 = CreateImageXObject(doc, (byte[])colorBytes.Clone(), width: 4);

            var (resolved1, resolved2) = ConsolidateAndResolve(doc, image1, image2);

            Assert.NotSame(resolved1, resolved2);
        }

        [Fact]
        public void ConsolidateImages_SameColorStreamDifferentDecodeArray_AreNotMerged()
        {
            var doc = new PdfDocument();
            var colorBytes = Encoding.ASCII.GetBytes("identical-color-stream");

            var image1 = CreateImageXObject(doc, colorBytes, decode: new PdfArray(doc, new PdfReal(0), new PdfReal(1)));
            var image2 = CreateImageXObject(doc, (byte[])colorBytes.Clone(), decode: new PdfArray(doc, new PdfReal(1), new PdfReal(0)));

            var (resolved1, resolved2) = ConsolidateAndResolve(doc, image1, image2);

            Assert.NotSame(resolved1, resolved2);
        }

        [Fact]
        public void ConsolidateImages_ColorSpaceIndirectIccProfileByteIdentical_AreMerged()
        {
            // /ColorSpace here is an indirect [/ICCBased <stream ref>] array - two distinct array/stream
            // objects with byte-identical profile content should still compare equal, proving the
            // dereference-and-recurse behavior isn't defeated by object identity alone.
            var doc = new PdfDocument();
            var colorBytes = Encoding.ASCII.GetBytes("identical-color-stream");
            var profileBytes = Encoding.ASCII.GetBytes("icc-profile-bytes");

            var profile1 = CreateIccProfileStream(doc, (byte[])profileBytes.Clone());
            var profile2 = CreateIccProfileStream(doc, (byte[])profileBytes.Clone());
            var colorSpace1 = new PdfArray(doc, new PdfName("/ICCBased"), profile1.Reference);
            var colorSpace2 = new PdfArray(doc, new PdfName("/ICCBased"), profile2.Reference);
            doc.Internals.AddObject(colorSpace1);
            doc.Internals.AddObject(colorSpace2);

            var image1 = CreateImageXObject(doc, colorBytes);
            image1.Elements["/ColorSpace"] = colorSpace1.Reference;
            var image2 = CreateImageXObject(doc, (byte[])colorBytes.Clone());
            image2.Elements["/ColorSpace"] = colorSpace2.Reference;

            var (resolved1, resolved2) = ConsolidateAndResolve(doc, image1, image2);

            Assert.Same(resolved1, resolved2);
        }

        [Fact]
        public void ConsolidateImages_ColorSpaceIndirectIccProfileDifferentBytes_AreNotMerged()
        {
            var doc = new PdfDocument();
            var colorBytes = Encoding.ASCII.GetBytes("identical-color-stream");

            var profile1 = CreateIccProfileStream(doc, Encoding.ASCII.GetBytes("icc-profile-a"));
            var profile2 = CreateIccProfileStream(doc, Encoding.ASCII.GetBytes("icc-profile-b"));
            var colorSpace1 = new PdfArray(doc, new PdfName("/ICCBased"), profile1.Reference);
            var colorSpace2 = new PdfArray(doc, new PdfName("/ICCBased"), profile2.Reference);
            doc.Internals.AddObject(colorSpace1);
            doc.Internals.AddObject(colorSpace2);

            var image1 = CreateImageXObject(doc, colorBytes);
            image1.Elements["/ColorSpace"] = colorSpace1.Reference;
            var image2 = CreateImageXObject(doc, (byte[])colorBytes.Clone());
            image2.Elements["/ColorSpace"] = colorSpace2.Reference;

            var (resolved1, resolved2) = ConsolidateAndResolve(doc, image1, image2);

            Assert.NotSame(resolved1, resolved2);
        }

        [Fact]
        public void ConsolidateImages_SameColorStreamDifferentInterpolate_AreNotMerged()
        {
            var doc = new PdfDocument();
            var colorBytes = Encoding.ASCII.GetBytes("identical-color-stream");

            var image1 = CreateImageXObject(doc, colorBytes);
            image1.Elements["/Interpolate"] = new PdfBoolean(true);
            var image2 = CreateImageXObject(doc, (byte[])colorBytes.Clone());
            image2.Elements["/Interpolate"] = new PdfBoolean(false);

            var (resolved1, resolved2) = ConsolidateAndResolve(doc, image1, image2);

            Assert.NotSame(resolved1, resolved2);
        }
    }
}
